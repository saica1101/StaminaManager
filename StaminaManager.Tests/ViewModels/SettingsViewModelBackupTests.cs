using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
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
        await using PreviewContext context = await PreviewContext.CreateAsync();

        Task<BackupPreview> previewTask = context.ViewModel
            .PreviewRestoreAsync("backup.staminabackup");

        Assert.IsTrue(context.ViewModel.IsBackupBusy);
        Assert.IsFalse(context.ViewModel.IsSettingsInteractionEnabled);
        await context.Backup.PreviewStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
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
        await using PreviewContext context = await PreviewContext.CreateAsync();
        Task<BackupPreview> previewTask = context.ViewModel
            .PreviewRestoreAsync("backup.staminabackup");
        await context.Backup.PreviewStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(5));
        Assert.IsTrue(context.ViewModel.IsBackupBusy);
        Assert.IsFalse(context.ViewModel.IsSettingsInteractionEnabled);

        context.Backup.FailPreview(new IOException("preview failure"));

        await Assert.ThrowsExactlyAsync<IOException>(() => previewTask);
        Assert.IsFalse(context.ViewModel.IsBackupBusy);
        Assert.IsTrue(context.ViewModel.IsSettingsInteractionEnabled);
    }

    [TestMethod]
    public async Task PreviewRestoreAsync_キャンセル後に設定操作を再び有効にする()
    {
        await using PreviewContext context = await PreviewContext.CreateAsync();
        using CancellationTokenSource cancellation = new();
        Task<BackupPreview> previewTask = context.ViewModel
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

        public static async Task<PreviewContext> CreateAsync()
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

    private sealed class BlockingBackupService : IBackupService
    {
        private readonly TaskCompletionSource<BackupPreview>
            _previewCompletion = new(
                TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource PreviewStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int ExportCount { get; private set; }

        public int RestoreCount { get; private set; }

        public Task ExportAsync(
            string destinationPath,
            CancellationToken cancellationToken)
        {
            ExportCount++;
            return Task.CompletedTask;
        }

        public async Task<BackupPreview> PreviewAsync(
            string sourcePath,
            CancellationToken cancellationToken)
        {
            PreviewStarted.TrySetResult();
            return await _previewCompletion.Task.WaitAsync(cancellationToken);
        }

        public Task<BackupRestoreResult> RestoreAsync(
            string sourcePath,
            CancellationToken cancellationToken)
        {
            RestoreCount++;
            throw new NotSupportedException();
        }

        public Task<BackupRestoreResult> RestoreAndPublishAsync(
            string sourcePath,
            Func<DataEnvelope, CancellationToken, Task>
                publishCommittedDataAsync,
            CancellationToken cancellationToken)
        {
            RestoreCount++;
            throw new NotSupportedException();
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
                Theme: "Light",
                Backdrop: "Mica",
                NotificationsEnabled: true,
                CloseBehavior: "MinimizeToTray",
                StartupEnabled: false));

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

        public BackdropResult Apply(BackdropKind requestedBackdrop) => new(
            requestedBackdrop,
            requestedBackdrop,
            BackdropFallbackReason.None,
            ErrorMessage: null);

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
