using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Resources;
using StaminaManager.Tests.Backup;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.ViewModels;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class SettingsViewModelBackupTests
{
    [TestMethod]
    public async Task PreviewRestoreAsync_実行中は全設定操作と再入を拒否する()
    {
        await using PreviewContext context = await PreviewContext.CreateAsync(
            SettingsEnglishResourceFixture.Create());

        Task<PreparedBackupRestore> previewTask = context.ViewModel
            .PreviewRestoreAsync("backup.staminabackup");

        Assert.IsTrue(context.ViewModel.IsBackupBusy);
        Assert.IsFalse(context.ViewModel.IsSettingsInteractionEnabled);
        await context.Backup.PreviewStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.AreEqual(
            "Checking the backup contents...",
            context.ViewModel.BackupStatusText);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => context.ViewModel.PreviewRestoreAsync("second"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => context.ViewModel.ExportBackupAsync("export"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => context.ViewModel.RestoreBackupAsync(
                "restore",
                isReplacementConfirmed: true));
        Assert.IsFalse(await context.ViewModel.SetCloseBehaviorAsync(
            CloseBehavior.Exit));
        Assert.AreEqual(0, context.Backup.ExportCount);
        Assert.AreEqual(0, context.Backup.RestoreCount);
        Assert.AreEqual(0, context.Store.SaveCount);

        context.Backup.CompletePreview();
        _ = await previewTask;

        Assert.IsFalse(context.ViewModel.IsBackupBusy);
        Assert.IsTrue(context.ViewModel.IsSettingsInteractionEnabled);
    }

    [TestMethod]
    public async Task PreviewRestoreAsync_例外後に設定操作を再び有効にする()
    {
        await using PreviewContext context = await PreviewContext.CreateAsync(
            SettingsEnglishResourceFixture.Create());
        Task<PreparedBackupRestore> previewTask = context.ViewModel
            .PreviewRestoreAsync("backup.staminabackup");
        await context.Backup.PreviewStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.IsTrue(context.ViewModel.IsBackupBusy);
        Assert.IsFalse(context.ViewModel.IsSettingsInteractionEnabled);

        context.Backup.FailPreview(new IOException("preview failure"));

        await Assert.ThrowsExactlyAsync<IOException>(() => previewTask);
        Assert.AreEqual(
            "The backup could not be read.",
            context.ViewModel.BackupStatusText);
        Assert.AreEqual(
            "The selected backup could not be read. Choose another backup and try again.",
            context.ViewModel.InfoBarMessage);
        Assert.AreEqual(
            "Backup could not be read",
            context.ViewModel.InfoBarTitle);
        Assert.IsFalse(context.ViewModel.IsBackupBusy);
        Assert.IsTrue(context.ViewModel.IsSettingsInteractionEnabled);
    }

    [TestMethod]
    public async Task ExportBackupAsync_英語resourceで成功文言を表示する()
    {
        await using PreviewContext context = await PreviewContext.CreateAsync(
            SettingsEnglishResourceFixture.Create());

        await context.ViewModel.ExportBackupAsync("export");

        Assert.AreEqual(
            "Backup created.",
            context.ViewModel.BackupStatusText);
        Assert.AreEqual(
            "The backup was saved to the selected location.",
            context.ViewModel.InfoBarMessage);
        Assert.AreEqual(
            "Backup completed",
            context.ViewModel.InfoBarTitle);
        Assert.IsFalse(context.ViewModel.IsBackupBusy);
    }

    [TestMethod]
    public async Task ExportBackupAsync_失敗時に英語InfoBarを表示する()
    {
        await using PreviewContext context = await PreviewContext.CreateAsync(
            SettingsEnglishResourceFixture.Create());
        context.Backup.ExportException = new IOException("test failure");

        await context.ViewModel.ExportBackupAsync("export");

        Assert.AreEqual(
            "The backup could not be created.",
            context.ViewModel.BackupStatusText);
        Assert.AreEqual(
            "The backup could not be created. Try again.",
            context.ViewModel.InfoBarMessage);
        Assert.AreEqual(
            "Backup failed",
            context.ViewModel.InfoBarTitle);
        Assert.IsFalse(context.ViewModel.IsBackupBusy);
    }

    [TestMethod]
    public async Task RestoreBackupAsync_失敗時に英語InfoBarを表示する()
    {
        await using PreviewContext context = await PreviewContext.CreateAsync(
            SettingsEnglishResourceFixture.Create());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => context.ViewModel.RestoreBackupAsync(
                "restore",
                isReplacementConfirmed: true));

        Assert.AreEqual(
            "The backup could not be restored.",
            context.ViewModel.BackupStatusText);
        Assert.AreEqual(
            "The backup could not be restored. Your current data was not replaced.",
            context.ViewModel.InfoBarMessage);
        Assert.AreEqual(
            "Restore failed",
            context.ViewModel.InfoBarTitle);
        Assert.IsFalse(context.ViewModel.IsBackupBusy);
        Assert.AreEqual(0, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task PreviewRestoreAsync_キャンセル後に設定操作を再び有効にする()
    {
        await using PreviewContext context = await PreviewContext.CreateAsync();
        using CancellationTokenSource cancellation = new();
        Task<PreparedBackupRestore> previewTask = context.ViewModel
            .PreviewRestoreAsync(
                "backup.staminabackup",
                cancellation.Token);
        await context.Backup.PreviewStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.IsTrue(context.ViewModel.IsBackupBusy);
        Assert.IsFalse(context.ViewModel.IsSettingsInteractionEnabled);

        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => previewTask);
        Assert.IsFalse(context.ViewModel.IsBackupBusy);
        Assert.IsTrue(context.ViewModel.IsSettingsInteractionEnabled);
    }

    [TestMethod]
    public async Task CancelPreparedRestoreAsync_previewのsessionIdを破棄する()
    {
        await using PreviewContext context = await PreviewContext.CreateAsync();
        Task<PreparedBackupRestore> previewTask = context.ViewModel
            .PreviewRestoreAsync("backup.staminabackup");
        await context.Backup.PreviewStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        context.Backup.CompletePreview();
        PreparedBackupRestore prepared = await previewTask;

        await context.ViewModel.CancelPreparedRestoreAsync(
            prepared.SessionId,
            CancellationToken.None);

        Assert.AreEqual(1, context.Backup.CancelCount);
        Assert.AreEqual(
            prepared.SessionId,
            context.Backup.LastCanceledSessionId);
        Assert.IsFalse(context.ViewModel.IsBackupBusy);
        Assert.IsTrue(context.ViewModel.IsSettingsInteractionEnabled);
    }

    private sealed class PreviewContext : IAsyncDisposable
    {
        private PreviewContext(
            RestoreWorkflowTestStore store,
            CountingDataStore countingStore,
            BlockingBackupService backup,
            SettingsViewModel viewModel)
        {
            TestStore = store;
            Store = countingStore;
            Backup = backup;
            ViewModel = viewModel;
        }

        public RestoreWorkflowTestStore TestStore { get; }

        public CountingDataStore Store { get; }

        public BlockingBackupService Backup { get; }

        public SettingsViewModel ViewModel { get; }

        public static async Task<PreviewContext> CreateAsync(
            IAppResourceService? resources = null)
        {
            RestoreWorkflowTestStore testStore =
                await RestoreWorkflowTestStore.CreateAsync(
                    "game",
                    startupEnabled: false);
            DataEnvelope envelope = (await testStore.Store.LoadAsync(
                CancellationToken.None)).Envelope!;
            CountingDataStore store = new(envelope);
            GameManager manager = new(
                store,
                new FakeClock(DateTimeOffset.UtcNow),
                envelope.Settings);
            await manager.InitializeAsync(envelope, CancellationToken.None);
            BlockingBackupService backup = new();
            PreviewServices services = new();
            AppCoordinator app = new(
                store,
                manager,
                new RecordingUiDispatcher(),
                services,
                services,
                services,
                services,
                services,
                new RestoreCoordinator(backup, manager));
            SettingsViewModel viewModel = new(
                manager,
                services,
                services,
                services,
                services,
                services,
                services,
                resources ?? new AppResourceService(resourceId => resourceId),
                app);
            viewModel.MarkReady();
            return new PreviewContext(testStore, store, backup, viewModel);
        }

        public ValueTask DisposeAsync() => TestStore.DisposeAsync();
    }

    private sealed class CountingDataStore(DataEnvelope envelope)
        : ILocalDataStore
    {
        public int SaveCount { get; private set; }

        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new DataLoadResult(
                    DataLoadStatus.Primary,
                    envelope,
                    "primary",
                    "recovery"));

        public Task SaveAsync(
            DataEnvelope value,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class BlockingBackupService
        : IBackupService,
          IPreparedBackupCommitter
    {
        private readonly TaskCompletionSource<BackupPreview>
            _previewCompletion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PreviewStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ExportCount { get; private set; }

        public Exception? ExportException { get; set; }

        public int RestoreCount { get; private set; }

        public int CancelCount { get; private set; }

        public string? LastCanceledSessionId { get; private set; }

        public Task ExportAsync(
            string destinationPath,
            CancellationToken cancellationToken)
        {
            ExportCount++;
            if (ExportException is not null)
            {
                return Task.FromException(ExportException);
            }

            return Task.CompletedTask;
        }

        public async Task<BackupPreview> PreviewAsync(
            string sourcePath,
            CancellationToken cancellationToken)
        {
            PreviewStarted.TrySetResult();
            return await _previewCompletion.Task.WaitAsync(cancellationToken);
        }

        public async Task<PreparedBackupRestore> PrepareRestoreAsync(
            string sourcePath,
            CancellationToken cancellationToken) => new(
                "test-session",
                await PreviewAsync(sourcePath, cancellationToken));

        public Task<BackupRestoreResult> CommitPreparedRestoreAsync(
            string sessionId,
            Func<DataEnvelope, CancellationToken, Task>
                publishCommittedDataAsync,
            CancellationToken cancellationToken)
        {
            RestoreCount++;
            throw new NotSupportedException();
        }

        public Task CancelPreparedRestoreAsync(
            string sessionId,
            CancellationToken cancellationToken)
        {
            CancelCount++;
            LastCanceledSessionId = sessionId;
            return Task.CompletedTask;
        }

        public Task<BackupRestoreResult?> ResumeAsync(
            CancellationToken cancellationToken) => Task.FromResult<
                BackupRestoreResult?>(null);

        public Task AcknowledgeDerivedStateAsync(
            CancellationToken cancellationToken) => Task.CompletedTask;

        public void CompletePreview() => _previewCompletion.TrySetResult(
            new BackupPreview(
                GameCount: 1,
                ImageCount: 0,
                Theme: AppTheme.Light,
                Backdrop: BackdropKind.Mica,
                NotificationsEnabled: true,
                CloseBehavior: CloseBehavior.MinimizeToTray,
                StartupEnabled: false,
                AcrylicTintOpacityPercent: 80,
                Language: AppLanguage.Japanese));

        public void FailPreview(Exception exception) =>
            _previewCompletion.TrySetException(exception);
    }

    private sealed class PreviewServices
        : IThemeService,
          IBackdropService,
          IStartupService,
          IWindowStateService,
          INotificationReconciler,
          INotificationPermissionService,
          ISettingsLauncher
    {
        public AppDisplayMode CurrentDisplayMode { get; private set; }

        public AppTheme ResolveInitialTheme() => AppTheme.Light;

        public ThemeResult Apply(AppTheme requestedTheme) => new(
            requestedTheme,
            requestedTheme,
            IsApplied: true,
            ErrorMessage: null);

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
            CancellationToken cancellationToken) => Task.FromResult(new
                StartupChangeResult(
                    new StartupStatus(isEnabled
                        ? StartupState.Enabled
                        : StartupState.Disabled),
                    IsApplied: true,
                    StartupFailureReason.None));

        public void ApplyDisplayMode(AppDisplayMode displayMode) =>
            CurrentDisplayMode = displayMode;

        public void CaptureCurrent()
        {
        }

        public Task<NotificationReconcileResult> ReconcileAsync(
            IReadOnlyCollection<GameEntry> games,
            AppSettings settings,
            CancellationToken cancellationToken) => Task.FromResult(
                NotificationReconcileResult.Success);

        Task<NotificationPermissionStatus>
            INotificationPermissionService.GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new NotificationPermissionStatus(
                    NotificationPermissionState.Enabled));

        public Task<bool> OpenNotificationSettingsAsync(
            CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
