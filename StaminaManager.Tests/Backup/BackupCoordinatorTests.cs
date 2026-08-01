using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using StaminaManager.Infrastructure.Backup;
using StaminaManager.Infrastructure.Persistence;
using StaminaManager.Tests.TestDoubles;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;

namespace StaminaManager.Tests.Backup;

[TestClass]
public sealed class BackupCoordinatorTests
{
    [TestMethod]
    public async Task ExportAsync_manifestとdataだけを書き端末固有台帳を除外する()
    {
        await using Context context = await Context.CreateAsync("current");
        await File.WriteAllTextAsync(
            Path.Combine(context.DataRoot, "notification-state.json"),
            "secret-device-state");
        string backupPath = Path.Combine(context.Root, "export.staminabackup");

        await context.Coordinator.ExportAsync(
            backupPath,
            CancellationToken.None);

        using ZipArchive archive = ZipFile.OpenRead(backupPath);
        CollectionAssert.AreEquivalent(
            new[] { "manifest.json", "data.json" },
            archive.Entries.Select(entry => entry.FullName).ToArray());
        Assert.IsFalse(archive.Entries.Any(entry =>
            entry.FullName.Contains("notification-state", StringComparison.Ordinal)));
        Assert.IsFalse(archive.Entries.Any(entry =>
            Path.IsPathRooted(entry.FullName)));

        using JsonDocument manifest = await ReadJsonAsync(
            archive.GetEntry("manifest.json")!);
        using JsonDocument data = await ReadJsonAsync(
            archive.GetEntry("data.json")!);
        Assert.AreEqual(
            2,
            manifest.RootElement.GetProperty("dataSchemaVersion").GetInt32());
        Assert.AreEqual(
            2,
            data.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [TestMethod]
    public void ExportAsync_画像実サイズ合計が展開上限を超える場合は事前拒否する()
    {
        Assert.ThrowsExactly<InvalidDataException>(() =>
            BackupCoordinator.EnsureExpandedAssetLengthsWithinLimit(
            [BackupLimits.MaxExpandedBytes, 1]));
    }

    [TestMethod]
    [DataRow(RestoreJournalStage.Validated, "old")]
    [DataRow(RestoreJournalStage.Staged, "old")]
    [DataRow(RestoreJournalStage.LocalCommitted, "new")]
    [DataRow(RestoreJournalStage.Completed, "new")]
    public async Task RestoreAsync_障害段階に応じたdataを正とする(
        RestoreJournalStage failureStage,
        string expectedName)
    {
        await using Context source = await Context.CreateAsync("new");
        string backupPath = Path.Combine(source.Root, "new.staminabackup");
        await source.Coordinator.ExportAsync(
            backupPath,
            CancellationToken.None);
        await using Context destination = await Context.CreateAsync("old");
        BackupCoordinator coordinator = destination.CreateCoordinator(stage =>
        {
            if (stage == failureStage)
            {
                throw new InjectedRestoreException(stage);
            }
        });
        GameManager manager = new(
            destination.Store,
            new FakeClock(DateTimeOffset.UtcNow),
            AppSettings.CreateDefault(AppTheme.Light));
        await manager.InitializeAsync(
            (await destination.Store.LoadAsync(
                CancellationToken.None)).Envelope!,
            CancellationToken.None);
        RestoreCoordinator restore = new(coordinator, manager);

        if (failureStage is RestoreJournalStage.Validated
            or RestoreJournalStage.Staged)
        {
            await Assert.ThrowsExactlyAsync<InjectedRestoreException>(
                () => coordinator.PrepareRestoreAsync(
                    backupPath,
                    CancellationToken.None));
        }
        else
        {
            PreparedBackupRestore prepared =
                await coordinator.PrepareRestoreAsync(
                backupPath,
                CancellationToken.None);
            if (failureStage == RestoreJournalStage.LocalCommitted)
            {
                BackupRestoreResult result =
                    await restore.CommitPreparedAsync(
                        prepared.SessionId,
                        isReplacementConfirmed: true,
                        CancellationToken.None);
                Assert.IsTrue(result.IsPartial);
            }
            else
            {
                _ = await restore.CommitPreparedAsync(
                    prepared.SessionId,
                    isReplacementConfirmed: true,
                    CancellationToken.None);
                await Assert.ThrowsExactlyAsync<InjectedRestoreException>(
                    () => coordinator.AcknowledgeDerivedStateAsync(
                        CancellationToken.None));
            }
        }

        DataLoadResult load = await destination.Store.LoadAsync(
            CancellationToken.None);
        Assert.AreEqual(expectedName, load.Envelope!.Games[0].Name);
        if (failureStage >= RestoreJournalStage.LocalCommitted)
        {
            string previousData = Directory.EnumerateFiles(
                Path.Combine(destination.DataRoot, ".backup", "previous"),
                "data.json",
                SearchOption.TopDirectoryOnly).Single();
            StringAssert.Contains(
                await File.ReadAllTextAsync(previousData),
                "old");
        }
    }

    [TestMethod]
    public async Task ResumeAsync_LocalCommitted後は新dataと再調整要求を返す()
    {
        await using Context source = await Context.CreateAsync("new");
        string backupPath = Path.Combine(source.Root, "new.staminabackup");
        await source.Coordinator.ExportAsync(
            backupPath,
            CancellationToken.None);
        await using Context destination = await Context.CreateAsync("old");
        BackupCoordinator failing = destination.CreateCoordinator(stage =>
        {
            if (stage == RestoreJournalStage.LocalCommitted)
            {
                throw new InjectedRestoreException(stage);
            }
        });
        PreparedBackupRestore prepared = await failing.PrepareRestoreAsync(
            backupPath,
            CancellationToken.None);
        GameManager manager = new(
            destination.Store,
            new FakeClock(DateTimeOffset.UtcNow),
            AppSettings.CreateDefault(AppTheme.Light));
        await manager.InitializeAsync(
            (await destination.Store.LoadAsync(
                CancellationToken.None)).Envelope!,
            CancellationToken.None);
        RestoreCoordinator restore = new(failing, manager);
        BackupRestoreResult committed =
            await restore.CommitPreparedAsync(
                prepared.SessionId,
                isReplacementConfirmed: true,
                CancellationToken.None);
        Assert.IsTrue(committed.IsPartial);

        BackupRestoreResult? resumed =
            await destination.Coordinator.ResumeAsync(
                CancellationToken.None);

        Assert.IsNotNull(resumed);
        Assert.IsTrue(resumed.RequiresDerivedStateRetry);
        Assert.AreEqual("new", resumed.Data.Games[0].Name);
    }

    [TestMethod]
    public async Task RestoreWorkflow_Startup拒否をOff保存し通知失敗を再試行状態にする()
    {
        await using Context source = await Context.CreateAsync("new");
        DataLoadResult sourceLoad = await source.Store.LoadAsync(
            CancellationToken.None);
        await source.Store.SaveAsync(
            sourceLoad.Envelope! with
            {
                Settings = sourceLoad.Envelope.Settings with
                {
                    StartupEnabled = true,
                },
            },
            CancellationToken.None);
        string backupPath = Path.Combine(source.Root, "new.staminabackup");
        await source.Coordinator.ExportAsync(
            backupPath,
            CancellationToken.None);

        await using Context destination = await Context.CreateAsync("old");
        GameManager manager = new(
            destination.Store,
            new FakeClock(DateTimeOffset.UtcNow),
            AppSettings.CreateDefault(AppTheme.Light));
        RestoreCoordinator restore = new(destination.Coordinator, manager);
        AppCoordinator app = new(
            destination.Store,
            manager,
            new RecordingUiDispatcher(),
            new PassThroughTheme(),
            new PassThroughBackdrop(),
            new DisabledStartup(),
            new PassThroughWindow(),
            new FailingNotifications(),
            restore);
        await app.InitializeAsync(CancellationToken.None);

        PreparedBackupRestore prepared = await app.PreviewRestoreAsync(
            backupPath,
            CancellationToken.None);
        _ = await app.RestoreBackupAsync(
            prepared.SessionId,
            isReplacementConfirmed: true,
            CancellationToken.None);

        Assert.AreEqual("new", manager.Games[0].Name);
        Assert.IsFalse(manager.CurrentData.Settings.StartupEnabled);
        Assert.IsTrue(app.LastNotificationReconcileResult!.HasFailures);
        Assert.IsNotNull(await destination.Coordinator.ResumeAsync(
            CancellationToken.None));
    }

    private sealed class InjectedRestoreException(RestoreJournalStage stage)
        : Exception(stage.ToString());

    private sealed class PassThroughTheme : IThemeService
    {
        public AppTheme ResolveInitialTheme() => AppTheme.Light;

        public ThemeResult Apply(AppTheme requestedTheme) => new(
            requestedTheme,
            requestedTheme,
            IsApplied: true,
            ErrorMessage: null);
    }

    private sealed class PassThroughBackdrop : IBackdropService
    {
        public BackdropResult Apply(BackdropKind requestedBackdrop) => new(
            requestedBackdrop,
            requestedBackdrop,
            BackdropFallbackReason.None,
            ErrorMessage: null);
    }

    private sealed class DisabledStartup : IStartupService
    {
        public Task<StartupStatus> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new StartupStatus(StartupState.DisabledByUser));

        public Task<StartupChangeResult> SetEnabledAsync(
            bool isEnabled,
            CancellationToken cancellationToken) => Task.FromResult(new
                StartupChangeResult(
                    new StartupStatus(StartupState.DisabledByUser),
                    IsApplied: false,
                    StartupFailureReason.DisabledByUser));
    }

    private sealed class PassThroughWindow : IWindowStateService
    {
        public AppDisplayMode CurrentDisplayMode { get; private set; }

        public void ApplyDisplayMode(AppDisplayMode displayMode) =>
            CurrentDisplayMode = displayMode;

        public void CaptureCurrent()
        {
        }
    }

    private sealed class FailingNotifications : INotificationReconciler
    {
        public Task<NotificationReconcileResult> ReconcileAsync(
            IReadOnlyCollection<GameEntry> games,
            AppSettings settings,
            CancellationToken cancellationToken) => Task.FromResult(new
                NotificationReconcileResult(
                    ImmutableArray.Create(new NotificationReconcileIssue(
                        GameId: null,
                        NotificationDecisionError.None,
                        "InjectedFailure"))));
    }

    private sealed class Context : IAsyncDisposable
    {
        private Context(
            string root,
            TestPathProvider pathProvider,
            LocalDataStore store)
        {
            Root = root;
            PathProvider = pathProvider;
            Store = store;
            Coordinator = CreateCoordinator();
        }

        public string Root { get; }

        public string DataRoot => PathProvider.DataRootPath;

        public TestPathProvider PathProvider { get; }

        public LocalDataStore Store { get; }

        public BackupCoordinator Coordinator { get; }

        public static async Task<Context> CreateAsync(string gameName)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "StaminaManager.BackupTests",
                Guid.NewGuid().ToString("N"));
            TestPathProvider provider = new(Path.Combine(root, "Data"));
            LocalDataStore store = new(provider);
            Directory.CreateDirectory(provider.DataRootPath);
            await store.SaveAsync(
                CreateEnvelope(gameName),
                CancellationToken.None);
            return new Context(root, provider, store);
        }

        public BackupCoordinator CreateCoordinator(
            Action<RestoreJournalStage>? failureInjector = null) => new(
                new SafeZipReader(),
                Store,
                PathProvider,
                failureInjector);

        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }

            return ValueTask.CompletedTask;
        }

        private static DataEnvelope CreateEnvelope(string gameName)
        {
            GameEntry game = new(
                Guid.NewGuid(),
                gameName,
                BaseStamina: 1,
                MaxStamina: 100,
                RecoveryMinutes: 5,
                DateTimeOffset.UtcNow,
                ImageAssetId: null,
                SortOrder: 0,
                RecoverySeconds: 0,
                IsNotificationEnabled: true);
            return new DataEnvelope(
                DataEnvelope.CurrentSchemaVersion,
                ImmutableArray.Create(game),
                AppSettings.CreateDefault(AppTheme.Light) with
                {
                    SelectedCompactGameId = game.Id,
                });
        }
    }

    private sealed class TestPathProvider(string dataRootPath)
        : IAppDataPathProvider
    {
        public string DataRootPath { get; } = dataRootPath;
    }

    private static async Task<JsonDocument> ReadJsonAsync(
        ZipArchiveEntry entry)
    {
        await using Stream stream = entry.Open();
        return await JsonDocument.ParseAsync(stream);
    }
}
