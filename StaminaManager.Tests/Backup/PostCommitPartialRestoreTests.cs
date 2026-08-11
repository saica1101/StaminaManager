using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Backup;
using StaminaManager.Infrastructure.Resources;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.ViewModels;
using System.Collections.Immutable;

namespace StaminaManager.Tests.Backup;

[TestClass]
public sealed class PostCommitPartialRestoreTests
{
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task RestoreAsync_commit直後の取消またはjournal失敗でもnewDataをpublishする(
        bool cancelToken)
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync("new", false);
        string backupPath = await source.ExportAsync();
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync("old", false);
        GameManager manager = destination.CreateManager();
        await manager.InitializeAsync(
            (await destination.Store.LoadAsync(CancellationToken.None)).Envelope!,
            CancellationToken.None);
        using CancellationTokenSource cancellation = new();
        BackupCoordinator backup = destination.CreateBackup(
            journalWriteInjector: stage =>
            {
                if (stage != RestoreJournalStage.LocalCommitted)
                {
                    return;
                }

                if (cancelToken)
                {
                    cancellation.Cancel();
                    cancellation.Token.ThrowIfCancellationRequested();
                }

                throw new IOException("journal write failure");
            });
        PreparedBackupRestore prepared = await backup.PrepareRestoreAsync(
            backupPath,
            CancellationToken.None);

        BackupRestoreResult result = await new RestoreCoordinator(
                backup,
                manager)
            .CommitPreparedAsync(
                prepared.SessionId,
                true,
                cancellation.Token)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(result.IsCommitted);
        Assert.IsTrue(result.IsPartial);
        Assert.AreEqual("new", manager.Games[0].Name);
        Assert.AreEqual(
            "new",
            (await destination.Store.LoadAsync(CancellationToken.None))
                .Envelope!.Games[0].Name);
        Assert.IsNotNull(await backup.ResumeAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task RestoreAsync_LocalCommitted例外後もnewDataをpublishしてpartialを返す()
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync("new", false);
        string backupPath = await source.ExportAsync();
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync("old", false);
        GameManager manager = destination.CreateManager();
        await manager.InitializeAsync(
            (await destination.Store.LoadAsync(CancellationToken.None)).Envelope!,
            CancellationToken.None);
        BackupCoordinator backup = destination.CreateBackup(stage =>
        {
            if (stage == RestoreJournalStage.LocalCommitted)
            {
                throw new IOException("post-commit failure");
            }
        });
        RestoreCoordinator restore = new(backup, manager);
        PreparedBackupRestore prepared = await backup.PrepareRestoreAsync(
            backupPath,
            CancellationToken.None);

        BackupRestoreResult result = await restore.CommitPreparedAsync(
                prepared.SessionId,
                true,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(result.IsCommitted);
        Assert.IsTrue(result.IsPartial);
        Assert.IsTrue(result.RequiresDerivedStateRetry);
        Assert.AreEqual("new", manager.Games[0].Name);
        Assert.AreEqual(
            "new",
            (await destination.Store.LoadAsync(CancellationToken.None))
                .Envelope!.Games[0].Name);
        Assert.IsNotNull(await backup.ResumeAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task RestoreAsync_theme例外後もnewDataとVmを同期してpartialを返す()
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync(
                "new",
                startupEnabled: false);
        DataEnvelope sourceData = (await source.Store.LoadAsync(
            CancellationToken.None)).Envelope!;
        await source.Store.SaveAsync(
            sourceData with
            {
                Settings = sourceData.Settings with
                {
                    Theme = AppTheme.Dark,
                },
            },
            CancellationToken.None);
        string backupPath = await source.ExportAsync();
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
                "old",
                startupEnabled: false);
        GameManager manager = destination.CreateManager();
        PartialServices services = new();
        BackupCoordinator backup = destination.CreateBackup();
        AppCoordinator app = new(
            destination.Store,
            manager,
            new RecordingUiDispatcher(),
            services,
            services,
            services,
            services,
            services,
            new RestoreCoordinator(backup, manager));
        await app.InitializeAsync(CancellationToken.None);
        SettingsViewModel viewModel = new(
            manager,
            services,
            services,
            services,
            services,
            services,
            services,
            new AppResourceService(resourceId => resourceId),
            app);
        viewModel.MarkReady();

        PreparedBackupRestore prepared = await viewModel
            .PreviewRestoreAsync(
                backupPath,
                CancellationToken.None);
        services.ThrowForDarkTheme = true;

        BackupRestoreResult result = await viewModel.RestoreBackupAsync(
            prepared.SessionId,
            isReplacementConfirmed: true,
            CancellationToken.None);

        Assert.IsTrue(result.IsCommitted);
        Assert.IsTrue(result.IsPartial);
        Assert.IsTrue(result.RequiresDerivedStateRetry);
        Assert.AreEqual("new", manager.Games[0].Name);
        Assert.AreEqual(AppTheme.Dark, manager.CurrentData.Settings.Theme);
        Assert.AreEqual(AppTheme.Dark, viewModel.Theme);
        Assert.IsFalse(viewModel.IsBackupBusy);
        Assert.IsTrue(viewModel.IsSettingsInteractionEnabled);
        Assert.IsNotNull(await backup.ResumeAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task RestoreAsync_ack例外をthrowせずpartialとして返す()
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
        PartialServices services = new();
        BackupCoordinator backup = destination.CreateBackup(stage =>
        {
            if (stage == RestoreJournalStage.Completed)
            {
                throw new InvalidOperationException("ack failure");
            }
        });
        AppCoordinator app = new(
            destination.Store,
            manager,
            new RecordingUiDispatcher(),
            services,
            services,
            services,
            services,
            services,
            new RestoreCoordinator(backup, manager));
        await app.InitializeAsync(CancellationToken.None);
        PreparedBackupRestore prepared = await app.PreviewRestoreAsync(
            backupPath,
            CancellationToken.None);

        BackupRestoreResult result = await app.RestoreBackupAsync(
            prepared.SessionId,
            isReplacementConfirmed: true,
            CancellationToken.None);

        Assert.IsTrue(result.IsCommitted);
        Assert.IsTrue(result.IsPartial);
        Assert.IsTrue(result.RequiresDerivedStateRetry);
        Assert.AreEqual("new", manager.Games[0].Name);
        Assert.IsNotNull(await backup.ResumeAsync(CancellationToken.None));
    }

    [TestMethod]
    [DataRow("startup")]
    [DataRow("notification")]
    [DataRow("ack")]
    public async Task RestoreAsync_commit後取消をpartialへ変換する(string target)
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync("new", true);
        string backupPath = await source.ExportAsync();
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync("old", false);
        GameManager manager = destination.CreateManager();
        PartialServices services = new();
        BackupCoordinator backup = destination.CreateBackup(stage =>
        {
            if (target == "ack" && stage == RestoreJournalStage.Completed)
            {
                throw new OperationCanceledException("ack canceled");
            }
        });
        AppCoordinator app = new(
            destination.Store,
            manager,
            new RecordingUiDispatcher(),
            services,
            services,
            services,
            services,
            services,
            new RestoreCoordinator(backup, manager));
        await app.InitializeAsync(CancellationToken.None);
        services.CancelStartup = target == "startup";
        services.CancelNotifications = target == "notification";
        PreparedBackupRestore prepared = await app.PreviewRestoreAsync(
            backupPath,
            CancellationToken.None);

        BackupRestoreResult result = await app.RestoreBackupAsync(
                prepared.SessionId,
                true,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(result.IsCommitted);
        Assert.IsTrue(result.IsPartial);
        Assert.IsTrue(result.RequiresDerivedStateRetry);
        Assert.AreEqual("new", manager.Games[0].Name);
        Assert.IsNotNull(await backup.ResumeAsync(CancellationToken.None));
    }

    private sealed class PartialServices
        : IThemeService,
          IBackdropService,
          IStartupService,
          IWindowStateService,
          INotificationReconciler,
          INotificationPermissionService,
          ISettingsLauncher
    {
        public bool ThrowForDarkTheme { get; set; }

        public bool CancelStartup { get; set; }

        public bool CancelNotifications { get; set; }

        public AppDisplayMode CurrentDisplayMode { get; private set; }

        public AppTheme ResolveInitialTheme() => AppTheme.Light;

        public ThemeResult Apply(AppTheme requestedTheme)
        {
            if (ThrowForDarkTheme && requestedTheme == AppTheme.Dark)
            {
                throw new InvalidOperationException("theme failure");
            }

            return new ThemeResult(
                requestedTheme,
                requestedTheme,
                IsApplied: true,
                ErrorMessage: null);
        }

        public BackdropResult Apply(BackdropRequest request) => new(
            request.Kind,
            request.Kind,
            BackdropFallbackReason.None,
            ErrorMessage: null,
            request.Kind == BackdropKind.Acrylic
                ? request.AcrylicTintOpacityPercent
                : null);

        Task<StartupStatus> IStartupService.GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new StartupStatus(StartupState.Disabled));

        public Task<StartupChangeResult> SetEnabledAsync(
            bool isEnabled,
            CancellationToken cancellationToken)
        {
            if (CancelStartup)
            {
                throw new OperationCanceledException("startup canceled");
            }

            return Task.FromResult(new StartupChangeResult(
                    new StartupStatus(isEnabled
                        ? StartupState.Enabled
                        : StartupState.Disabled),
                    IsApplied: true,
                    StartupFailureReason.None));
        }

        public void ApplyDisplayMode(AppDisplayMode displayMode) =>
            CurrentDisplayMode = displayMode;

        public void CaptureCurrent()
        {
        }

        public Task<NotificationReconcileResult> ReconcileAsync(
            IReadOnlyCollection<GameEntry> games,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            if (CancelNotifications)
            {
                throw new OperationCanceledException(
                    "notification canceled");
            }

            return Task.FromResult(NotificationReconcileResult.Success);
        }

        Task<NotificationPermissionStatus>
            INotificationPermissionService.GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new NotificationPermissionStatus(
                    NotificationPermissionState.Enabled));

        public Task<bool> OpenNotificationSettingsAsync(
            CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
