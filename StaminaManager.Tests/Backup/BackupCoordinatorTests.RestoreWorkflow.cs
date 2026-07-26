using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Backup;
using StaminaManager.Infrastructure.Persistence;
using StaminaManager.Tests.TestDoubles;
using System.Collections.Immutable;

namespace StaminaManager.Tests.Backup;

[TestClass]
public sealed class BackupCoordinatorTests_RestoreWorkflow
{
    [TestMethod]
    public async Task Restore_Commit直後の通常設定保存はnew系を基準にする()
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync(
            "new",
            startupEnabled: false);
        string backupPath = await source.ExportAsync();
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
            "old",
            startupEnabled: false);
        GameManager manager = destination.CreateManager();
        Task<AppSettings>? competingSave = null;
        BackupCoordinator backup = destination.CreateBackup(stage =>
        {
            if (stage == RestoreJournalStage.LocalCommitted)
            {
                competingSave = manager.UpdateSettingsAsync(
                    settings => settings with
                    {
                        NotificationLeadMinutes = 99,
                    },
                    CancellationToken.None);
            }
        });
        RecordingDerivedServices services = new()
        {
            ShouldFailNotifications = true,
        };
        AppCoordinator app = CreateApp(
            destination,
            manager,
            backup,
            new RecordingStartupService(StartupState.Disabled),
            services);
        await app.InitializeAsync(CancellationToken.None);

        PreparedBackupRestore prepared = await app.PreviewRestoreAsync(
            backupPath,
            CancellationToken.None);
        _ = await app.RestoreBackupAsync(
                prepared.SessionId,
                isReplacementConfirmed: true,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await competingSave!.WaitAsync(TimeSpan.FromSeconds(5));

        DataLoadResult primary = await destination.Store.LoadAsync(
            CancellationToken.None);
        BackupRestoreResult? resumed = await backup.ResumeAsync(
            CancellationToken.None);
        Assert.AreEqual("new", primary.Envelope!.Games[0].Name);
        Assert.AreEqual("new", manager.Games[0].Name);
        Assert.AreEqual("new", resumed!.Data.Games[0].Name);
        Assert.AreEqual(99, primary.Envelope.Settings.NotificationLeadMinutes);
        Assert.AreEqual(
            99,
            manager.CurrentData.Settings.NotificationLeadMinutes);
        Assert.AreEqual(99, resumed.Data.Settings.NotificationLeadMinutes);
    }

    [TestMethod]
    public async Task Restore_StartupOn成功をWindowsへ適用する()
    {
        RecordingStartupService startup = new(StartupState.Disabled)
        {
            NextChange = new StartupChangeResult(
                new StartupStatus(StartupState.Enabled),
                IsApplied: true,
                StartupFailureReason.None),
        };

        WorkflowResult result = await RunRestoreAsync(
            desiredStartupEnabled: true,
            startup);

        CollectionAssert.AreEqual(new[] { true }, startup.SetRequests);
        Assert.IsTrue(result.Manager.CurrentData.Settings.StartupEnabled);
        Assert.IsTrue(result.App.IsStartupSynchronized);
        await result.DisposeAsync();
    }

    [TestMethod]
    public async Task Restore_StartupOn拒否時はOffを保存してpartialFailureにする()
    {
        RecordingStartupService startup = new(StartupState.Disabled)
        {
            NextChange = new StartupChangeResult(
                new StartupStatus(StartupState.DisabledByUser),
                IsApplied: false,
                StartupFailureReason.DisabledByUser),
        };

        WorkflowResult result = await RunRestoreAsync(
            desiredStartupEnabled: true,
            startup);

        CollectionAssert.AreEqual(new[] { true }, startup.SetRequests);
        Assert.IsFalse(result.Manager.CurrentData.Settings.StartupEnabled);
        Assert.IsFalse(result.App.IsStartupSynchronized);
        Assert.AreEqual(
            StartupFailureReason.DisabledByUser,
            result.App.StartupReconcileFailureReason);
        Assert.IsNotNull(await result.Backup.ResumeAsync(
            CancellationToken.None));
        await result.DisposeAsync();
    }

    [TestMethod]
    public async Task Restore_StartupOff成功をWindowsへ適用する()
    {
        RecordingStartupService startup = new(StartupState.Enabled)
        {
            NextChange = new StartupChangeResult(
                new StartupStatus(StartupState.Disabled),
                IsApplied: true,
                StartupFailureReason.None),
        };

        WorkflowResult result = await RunRestoreAsync(
            desiredStartupEnabled: false,
            startup);

        CollectionAssert.AreEqual(new[] { false }, startup.SetRequests);
        Assert.IsFalse(result.Manager.CurrentData.Settings.StartupEnabled);
        Assert.IsTrue(result.App.IsStartupSynchronized);
        await result.DisposeAsync();
    }

    [TestMethod]
    public async Task Restore_DerivedStateを固定順で一度だけ調整する()
    {
        RecordingDerivedServices services = new();
        RecordingStartupService startup = new(StartupState.Disabled)
        {
            NextChange = new StartupChangeResult(
                new StartupStatus(StartupState.Enabled),
                IsApplied: true,
                StartupFailureReason.None),
            Calls = services.Calls,
        };
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync(
            "new",
            startupEnabled: true);
        string backupPath = await source.ExportAsync();
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
            "old",
            startupEnabled: false);
        GameManager manager = destination.CreateManager();
        int overviewRefreshCount = 0;
        manager.GamesChanged += _ =>
        {
            overviewRefreshCount++;
            return Task.CompletedTask;
        };
        BackupCoordinator backup = destination.CreateBackup();
        AppCoordinator app = CreateApp(
            destination,
            manager,
            backup,
            startup,
            services);
        await app.InitializeAsync(CancellationToken.None);
        services.Calls.Clear();
        overviewRefreshCount = 0;

        PreparedBackupRestore prepared = await app.PreviewRestoreAsync(
            backupPath,
            CancellationToken.None);
        _ = await app.RestoreBackupAsync(
            prepared.SessionId,
            isReplacementConfirmed: true,
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                "Theme",
                "Backdrop",
                "Window",
                "Startup",
                "Notifications",
            },
            services.Calls);
        Assert.AreEqual(1, overviewRefreshCount);
    }

    private static async Task<WorkflowResult> RunRestoreAsync(
        bool desiredStartupEnabled,
        RecordingStartupService startup)
    {
        RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync(
            "new",
            desiredStartupEnabled);
        string backupPath = await source.ExportAsync();
        RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
            "old",
            startupEnabled: !desiredStartupEnabled);
        GameManager manager = destination.CreateManager();
        BackupCoordinator backup = destination.CreateBackup();
        AppCoordinator app = CreateApp(
            destination,
            manager,
            backup,
            startup,
            new RecordingDerivedServices());
        await app.InitializeAsync(CancellationToken.None);
        startup.SetRequests.Clear();
        PreparedBackupRestore prepared = await app.PreviewRestoreAsync(
            backupPath,
            CancellationToken.None);
        _ = await app.RestoreBackupAsync(
            prepared.SessionId,
            isReplacementConfirmed: true,
            CancellationToken.None);
        return new WorkflowResult(source, destination, manager, backup, app);
    }

    private static AppCoordinator CreateApp(
        RestoreWorkflowTestStore destination,
        GameManager manager,
        BackupCoordinator backup,
        IStartupService startup,
        RecordingDerivedServices services) => new(
            destination.Store,
            manager,
            new RecordingUiDispatcher(),
            services,
            services,
            startup,
            services,
            services,
            new RestoreCoordinator(backup, manager));

    private sealed record WorkflowResult(
        RestoreWorkflowTestStore Source,
        RestoreWorkflowTestStore Destination,
        GameManager Manager,
        BackupCoordinator Backup,
        AppCoordinator App) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Source.DisposeAsync();
            await Destination.DisposeAsync();
        }
    }

    private sealed class RecordingDerivedServices
        : IThemeService,
          IBackdropService,
          IWindowStateService,
          INotificationReconciler
    {
        public List<string> Calls { get; } = [];

        public bool ShouldFailNotifications { get; init; }

        public AppDisplayMode CurrentDisplayMode { get; private set; }

        public AppTheme ResolveInitialTheme() => AppTheme.Light;

        public ThemeResult Apply(AppTheme requestedTheme)
        {
            Calls.Add("Theme");
            return new ThemeResult(
                requestedTheme,
                requestedTheme,
                IsApplied: true,
                ErrorMessage: null);
        }

        public BackdropResult Apply(BackdropKind requestedBackdrop)
        {
            Calls.Add("Backdrop");
            return new BackdropResult(
                requestedBackdrop,
                requestedBackdrop,
                BackdropFallbackReason.None,
                ErrorMessage: null);
        }

        public void ApplyDisplayMode(AppDisplayMode displayMode)
        {
            Calls.Add("Window");
            CurrentDisplayMode = displayMode;
        }

        public void CaptureCurrent()
        {
        }

        public Task<NotificationReconcileResult> ReconcileAsync(
            IReadOnlyCollection<GameEntry> games,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            Calls.Add("Notifications");
            return Task.FromResult(ShouldFailNotifications
                ? new NotificationReconcileResult(
                    ImmutableArray.Create(new NotificationReconcileIssue(
                        GameId: null,
                        NotificationDecisionError.None,
                        "InjectedFailure")))
                : NotificationReconcileResult.Success);
        }
    }

    private sealed class RecordingStartupService(StartupState initialState)
        : IStartupService
    {
        public List<bool> SetRequests { get; } = [];

        public List<string>? Calls { get; init; }

        public StartupChangeResult? NextChange { get; init; }

        public Task<StartupStatus> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new StartupStatus(initialState));

        public Task<StartupChangeResult> SetEnabledAsync(
            bool isEnabled,
            CancellationToken cancellationToken)
        {
            Calls?.Add("Startup");
            SetRequests.Add(isEnabled);
            return Task.FromResult(NextChange ?? new StartupChangeResult(
                new StartupStatus(isEnabled
                    ? StartupState.Enabled
                    : StartupState.Disabled),
                IsApplied: true,
                StartupFailureReason.None));
        }
    }

}
