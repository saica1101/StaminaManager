using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.ViewModels;
using StaminaManager.Views;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class SettingsViewModelTests
{
    [TestMethod]
    public async Task SettingChanges_BeforeReadyAreRejectedWithoutSideEffects()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateNotReadyViewModel();

        bool[] results =
        [
            await viewModel.SetThemeAsync(AppTheme.Dark),
            await viewModel.SetBackdropAsync(BackdropKind.Acrylic),
            await viewModel.SetCloseBehaviorAsync(CloseBehavior.Exit),
            await viewModel.SetStartupEnabledAsync(isEnabled: true),
            await viewModel.SetNotificationsEnabledAsync(isEnabled: false),
            await viewModel.SetNotificationLeadMinutesAsync(30),
        ];

        CollectionAssert.AreEqual(
            new[] { false, false, false, false, false, false },
            results);
        Assert.IsFalse(viewModel.IsReady);
        Assert.IsEmpty(context.ThemeService.Requests);
        Assert.IsEmpty(context.BackdropService.Requests);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        StringAssert.Contains(viewModel.InfoBarMessage, "読み込み");
    }

    [TestMethod]
    public async Task MarkReady_AfterManagerInitializationEnablesChanges()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateNotReadyViewModel();

        Assert.IsTrue(viewModel.IsLoading);
        Assert.AreEqual(
            Microsoft.UI.Xaml.Visibility.Visible,
            viewModel.LoadingVisibility);

        viewModel.MarkReady();
        bool saved = await viewModel.SetCloseBehaviorAsync(
            CloseBehavior.Exit);

        Assert.IsTrue(viewModel.IsReady);
        Assert.IsFalse(viewModel.IsLoading);
        Assert.AreEqual(
            Microsoft.UI.Xaml.Visibility.Collapsed,
            viewModel.LoadingVisibility);
        Assert.IsTrue(saved);
        Assert.AreEqual(1, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task MarkFailed_StopsLoadingAndKeepsChangesDisabled()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateNotReadyViewModel();

        viewModel.MarkFailed();
        bool saved = await viewModel.SetCloseBehaviorAsync(
            CloseBehavior.Exit);

        Assert.AreEqual(
            SettingsInitializationState.Failed,
            viewModel.InitializationState);
        Assert.IsFalse(viewModel.IsReady);
        Assert.IsFalse(viewModel.IsLoading);
        Assert.IsTrue(viewModel.IsFailed);
        Assert.AreEqual(
            Microsoft.UI.Xaml.Visibility.Collapsed,
            viewModel.LoadingVisibility);
        Assert.AreEqual(
            Microsoft.UI.Xaml.Visibility.Visible,
            viewModel.FailedVisibility);
        Assert.IsFalse(saved);
        Assert.AreEqual(0, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task UnexpectedThemeServiceException_IsSafelyReported()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.ThemeService.ApplyException =
            new InvalidOperationException("service implementation detail");
        int synchronizationCount = 0;

        await SettingsChangeExecutor.ExecuteAsync(
            async () =>
            {
                await viewModel.SetThemeAsync(AppTheme.Dark);
            },
            viewModel.ReportUnexpectedFailure,
            () => synchronizationCount++);

        Assert.AreEqual(1, synchronizationCount);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        StringAssert.Contains(viewModel.InfoBarMessage, "設定を変更");
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "service implementation detail",
            StringComparison.Ordinal));
        Assert.AreEqual(0, context.Store.SaveCount);
    }

    [TestMethod]
    public void CreateDefault_UsesResolvedWindowsThemeAndDocumentedDefaults()
    {
        RecordingThemeService themeService = new(AppTheme.Dark);

        AppSettings settings = AppSettings.CreateDefault(
            themeService.ResolveInitialTheme());

        Assert.AreEqual(AppTheme.Dark, settings.Theme);
        Assert.AreEqual(BackdropKind.Mica, settings.Backdrop);
        Assert.IsTrue(settings.NotificationsEnabled);
        Assert.AreEqual(15, settings.NotificationLeadMinutes);
        Assert.AreEqual(
            CloseBehavior.MinimizeToTray,
            settings.CloseBehavior);
        Assert.IsFalse(settings.StartupEnabled);
    }

    [TestMethod]
    public async Task SetBackdropAsync_FallbackKeepsSavedPreference()
    {
        Context context = await Context.CreateAsync();
        context.BackdropService.NextResult = new BackdropResult(
            BackdropKind.Acrylic,
            BackdropKind.Solid,
            BackdropFallbackReason.HighContrast,
            "ハイ コントラストでは単色背景を使用します。");
        SettingsViewModel viewModel = context.CreateViewModel();

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Solid, viewModel.ActualBackdrop);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    [DataRow(BackdropFallbackReason.HighContrast, "コントラスト テーマ")]
    [DataRow(BackdropFallbackReason.TransparencyDisabled, "透明効果")]
    [DataRow(BackdropFallbackReason.RemoteSession, "リモート接続を終了")]
    [DataRow(BackdropFallbackReason.Unsupported, "別の背景")]
    [DataRow(BackdropFallbackReason.ApplyFailed, "再試行")]
    public async Task SetBackdropAsync_Fallback理由別の次アクションを案内して保存設定を維持する(
        BackdropFallbackReason reason,
        string expectedAction)
    {
        Context context = await Context.CreateAsync();
        context.BackdropService.NextResult = new BackdropResult(
            BackdropKind.Acrylic,
            BackdropKind.Solid,
            reason,
            "選択した背景を適用できませんでした。");
        SettingsViewModel viewModel = context.CreateViewModel();

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        StringAssert.Contains(viewModel.InfoBarMessage, expectedAction);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Solid, viewModel.ActualBackdrop);
        Assert.AreEqual(0, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task SetBackdropAsync_SaveFailureRollsBackAppliedBackdrop()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new IOException("save failure");

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        CollectionAssert.AreEqual(
            new[] { BackdropKind.Acrylic, BackdropKind.Mica },
            context.BackdropService.Requests);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.ActualBackdrop);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    public async Task SetBackdropAsync_SaveCancellationRollsBackThenRethrows()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new OperationCanceledException(
            "cancellation implementation detail");

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => viewModel.SetBackdropAsync(
                BackdropKind.Acrylic,
                CancellationToken.None));

        CollectionAssert.AreEqual(
            new[] { BackdropKind.Acrylic, BackdropKind.Mica },
            context.BackdropService.Requests);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.ActualBackdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Store.LastSaved.Settings.Backdrop);
    }

    [TestMethod]
    public async Task SetBackdropAsync_UnexpectedSaveFailureRollsBackAllState()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new NotSupportedException(
            "persistence implementation detail");

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        CollectionAssert.AreEqual(
            new[] { BackdropKind.Acrylic, BackdropKind.Mica },
            context.BackdropService.Requests);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Mica, viewModel.ActualBackdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Store.LastSaved.Settings.Backdrop);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "persistence implementation detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetBackdropAsync_RollbackExceptionPublishesSafeState()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new NotSupportedException(
            "persistence implementation detail");
        context.BackdropService.RollbackException =
            new InvalidOperationException(
                "rollback implementation detail");

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        CollectionAssert.AreEqual(
            new[]
            {
                BackdropKind.Acrylic,
                BackdropKind.Mica,
                BackdropKind.Solid,
            },
            context.BackdropService.Requests);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Solid, viewModel.ActualBackdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Store.LastSaved.Settings.Backdrop);
        StringAssert.Contains(viewModel.InfoBarMessage, "単色");
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "rollback implementation detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetBackdropAsync_SolidFallbackExceptionKeepsLastKnownActualState()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new NotSupportedException(
            "persistence implementation detail");
        context.BackdropService.RollbackException =
            new InvalidOperationException(
                "rollback implementation detail");
        context.BackdropService.SafeFallbackException =
            new InvalidOperationException(
                "fallback implementation detail");

        bool applied = await viewModel.SetBackdropAsync(
            BackdropKind.Acrylic,
            CancellationToken.None);

        Assert.IsFalse(applied);
        CollectionAssert.AreEqual(
            new[]
            {
                BackdropKind.Acrylic,
                BackdropKind.Mica,
                BackdropKind.Solid,
            },
            context.BackdropService.Requests);
        Assert.AreEqual(BackdropKind.Mica, viewModel.SelectedBackdrop);
        Assert.AreEqual(BackdropKind.Acrylic, viewModel.ActualBackdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(
            BackdropKind.Mica,
            context.Store.LastSaved.Settings.Backdrop);
        StringAssert.Contains(viewModel.InfoBarMessage, "確認できません");
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "implementation detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetThemeAsync_ApplyFailureKeepsLastGoodTheme()
    {
        Context context = await Context.CreateAsync();
        context.ThemeService.NextResult = new ThemeResult(
            AppTheme.Dark,
            AppTheme.Light,
            IsApplied: false,
            "テーマを適用できませんでした。");
        SettingsViewModel viewModel = context.CreateViewModel();

        bool applied = await viewModel.SetThemeAsync(
            AppTheme.Dark,
            CancellationToken.None);

        Assert.IsFalse(applied);
        Assert.AreEqual(
            AppTheme.Light,
            context.Manager.CurrentData.Settings.Theme);
        Assert.AreEqual(AppTheme.Light, viewModel.Theme);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    public async Task SetThemeAsync_UnexpectedSaveFailureRollsBackAllState()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new NotSupportedException(
            "persistence implementation detail");

        bool applied = await viewModel.SetThemeAsync(
            AppTheme.Dark,
            CancellationToken.None);

        Assert.IsFalse(applied);
        CollectionAssert.AreEqual(
            new[] { AppTheme.Dark, AppTheme.Light },
            context.ThemeService.Requests);
        Assert.AreEqual(AppTheme.Light, viewModel.Theme);
        Assert.AreEqual(
            AppTheme.Light,
            context.Manager.CurrentData.Settings.Theme);
        Assert.AreEqual(
            AppTheme.Light,
            context.Store.LastSaved.Settings.Theme);
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "persistence implementation detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SetThemeAsync_SaveCancellationPreservesCancellationWhenRollbackThrows()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new OperationCanceledException(
            "cancellation implementation detail");
        context.ThemeService.RollbackException =
            new InvalidOperationException(
                "rollback implementation detail");

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => viewModel.SetThemeAsync(
                AppTheme.Dark,
                CancellationToken.None));

        CollectionAssert.AreEqual(
            new[] { AppTheme.Dark, AppTheme.Light },
            context.ThemeService.Requests);
        Assert.AreEqual(AppTheme.Dark, viewModel.Theme);
        Assert.AreEqual(
            AppTheme.Light,
            context.Manager.CurrentData.Settings.Theme);
        Assert.AreEqual(
            AppTheme.Light,
            context.Store.LastSaved.Settings.Theme);
        StringAssert.Contains(viewModel.InfoBarMessage, "確認できません");
        Assert.IsFalse(viewModel.InfoBarMessage.Contains(
            "rollback implementation detail",
            StringComparison.Ordinal));
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(-1d)]
    [DataRow(0.5d)]
    [DataRow(525_601d)]
    public async Task SetNotificationLeadMinutesAsync_InvalidValueIsRejected(
        double value)
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();

        bool saved = await viewModel.SetNotificationLeadMinutesAsync(
            value,
            CancellationToken.None);

        Assert.IsFalse(saved);
        Assert.AreEqual(15, viewModel.NotificationLeadMinutes);
        Assert.AreEqual(15,
            context.Manager.CurrentData.Settings.NotificationLeadMinutes);
        Assert.AreEqual(0, context.Store.SaveCount);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(525_600d)]
    public async Task SetNotificationLeadMinutesAsync_BoundaryIsSaved(
        double value)
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();

        bool saved = await viewModel.SetNotificationLeadMinutesAsync(
            value,
            CancellationToken.None);

        Assert.IsTrue(saved);
        Assert.AreEqual((int)value, viewModel.NotificationLeadMinutes);
        Assert.AreEqual(
            (int)value,
            context.Manager.CurrentData.Settings.NotificationLeadMinutes);
        Assert.AreEqual(1, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task GeneralAndNotificationChanges_PublishAfterSave()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();

        Assert.IsTrue(await viewModel.SetCloseBehaviorAsync(
            CloseBehavior.Exit,
            CancellationToken.None));
        Assert.IsTrue(await viewModel.SetStartupEnabledAsync(
            isEnabled: true,
            CancellationToken.None));
        Assert.IsTrue(await viewModel.SetNotificationsEnabledAsync(
            isEnabled: false,
            CancellationToken.None));

        Assert.AreEqual(CloseBehavior.Exit, viewModel.CloseBehavior);
        Assert.IsTrue(viewModel.IsStartupEnabled);
        Assert.IsFalse(viewModel.AreNotificationsEnabled);
        Assert.AreEqual(3, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task GeneralChange_SaveFailureDoesNotPublish()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new IOException("save failure");

        bool saved = await viewModel.SetCloseBehaviorAsync(
            CloseBehavior.Exit,
            CancellationToken.None);

        Assert.IsFalse(saved);
        Assert.AreEqual(
            CloseBehavior.MinimizeToTray,
            viewModel.CloseBehavior);
        Assert.AreEqual(
            CloseBehavior.MinimizeToTray,
            context.Manager.CurrentData.Settings.CloseBehavior);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    public async Task ReservedActions_ReportPreparationWithoutClaimingSuccess()
    {
        Context context = await Context.CreateAsync();
        SettingsViewModel viewModel = context.CreateViewModel();
        List<SettingsPreparationAction> requests = [];
        viewModel.PreparationRequested += requests.Add;

        viewModel.PrepareBackupExport();
        viewModel.PrepareBackupImport();
        viewModel.SetWindowsNotificationAvailability(isAvailable: false);
        viewModel.PrepareWindowsNotificationSettings();

        CollectionAssert.AreEqual(
            new[]
            {
                SettingsPreparationAction.ExportBackup,
                SettingsPreparationAction.ImportBackup,
                SettingsPreparationAction.OpenWindowsNotificationSettings,
            },
            requests);
        Assert.IsFalse(viewModel.AreWindowsNotificationsAvailable);
        Assert.IsTrue(viewModel.IsOpenWindowsNotificationSettingsVisible);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        StringAssert.Contains(viewModel.InfoBarMessage, "準備中");
    }

    [TestMethod]
    public void AsyncSave_PublishesPropertiesOnCallingSynchronizationContext()
    {
        Context context = Context.CreateAsync().GetAwaiter().GetResult();
        SettingsViewModel viewModel = context.CreateViewModel();
        context.Store.ShouldBlockSave = true;
        PumpSynchronizationContext synchronizationContext = new();
        SynchronizationContext? previousContext =
            SynchronizationContext.Current;
        bool wasPublishedOnCallingContext = false;

        try
        {
            SynchronizationContext.SetSynchronizationContext(
                synchronizationContext);
            viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(
                    SettingsViewModel.CloseBehavior))
                {
                    wasPublishedOnCallingContext = ReferenceEquals(
                        SynchronizationContext.Current,
                        synchronizationContext);
                }
            };

            Task<bool> saveTask = viewModel.SetCloseBehaviorAsync(
                CloseBehavior.Exit,
                CancellationToken.None);
            context.Store.ReleaseSave();
            bool saved = synchronizationContext.RunUntil(saveTask);

            Assert.IsTrue(saved);
            Assert.IsTrue(wasPublishedOnCallingContext);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed record Context(
        MemoryDataStore Store,
        GameManager Manager,
        RecordingThemeService ThemeService,
        RecordingBackdropService BackdropService)
    {
        public static async Task<Context> CreateAsync()
        {
            AppSettings settings = AppSettings.CreateDefault(AppTheme.Light);
            DataEnvelope envelope = new(
                DataEnvelope.CurrentSchemaVersion,
                ImmutableArray<GameEntry>.Empty,
                settings);
            MemoryDataStore store = new(envelope);
            GameManager manager = new(
                store,
                new FakeClock(DateTimeOffset.UtcNow),
                settings);
            await manager.InitializeAsync(envelope, CancellationToken.None);
            return new Context(
                store,
                manager,
                new RecordingThemeService(AppTheme.Light),
                new RecordingBackdropService());
        }

        public SettingsViewModel CreateViewModel()
        {
            SettingsViewModel viewModel = CreateNotReadyViewModel();
            viewModel.MarkReady();
            return viewModel;
        }

        public SettingsViewModel CreateNotReadyViewModel() => new(
            Manager,
            ThemeService,
            BackdropService);
    }

    private sealed class RecordingThemeService(AppTheme initialTheme)
        : IThemeService
    {
        public List<AppTheme> Requests { get; } = [];

        public ThemeResult? NextResult { get; set; }

        public Exception? ApplyException { get; set; }

        public Exception? RollbackException { get; set; }

        public AppTheme ResolveInitialTheme() => initialTheme;

        public ThemeResult Apply(AppTheme requestedTheme)
        {
            Requests.Add(requestedTheme);
            if (Requests.Count == 2 && RollbackException is not null)
            {
                throw RollbackException;
            }

            if (ApplyException is not null)
            {
                throw ApplyException;
            }

            ThemeResult result = NextResult ?? new ThemeResult(
                requestedTheme,
                requestedTheme,
                IsApplied: true,
                ErrorMessage: null);
            NextResult = null;
            return result;
        }
    }

    private sealed class RecordingBackdropService : IBackdropService
    {
        public List<BackdropKind> Requests { get; } = [];

        public BackdropResult? NextResult { get; set; }

        public Exception? RollbackException { get; set; }

        public Exception? SafeFallbackException { get; set; }

        public BackdropResult Apply(BackdropKind requestedBackdrop)
        {
            Requests.Add(requestedBackdrop);
            if (Requests.Count == 2 && RollbackException is not null)
            {
                throw RollbackException;
            }

            if (Requests.Count == 3 && SafeFallbackException is not null)
            {
                throw SafeFallbackException;
            }

            BackdropResult result = NextResult ?? new BackdropResult(
                requestedBackdrop,
                requestedBackdrop,
                BackdropFallbackReason.None,
                ErrorMessage: null);
            NextResult = null;
            return result;
        }
    }

    private sealed class MemoryDataStore : ILocalDataStore
    {
        private readonly TaskCompletionSource _continueSave = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly DataEnvelope _envelope;

        public MemoryDataStore(DataEnvelope envelope)
        {
            _envelope = envelope;
            LastSaved = envelope;
        }

        public Exception? SaveException { get; set; }

        public int SaveCount { get; private set; }

        public DataEnvelope LastSaved { get; private set; }

        public bool ShouldBlockSave { get; set; }

        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new DataLoadResult(
                    DataLoadStatus.Primary,
                    _envelope,
                    "primary",
                    "recovery"));

        public async Task SaveAsync(
            DataEnvelope value,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            if (ShouldBlockSave)
            {
                await _continueSave.Task.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }

            if (SaveException is not null)
            {
                throw SaveException;
            }

            LastSaved = value;
        }

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void ReleaseSave() => _continueSave.TrySetResult();
    }

    private sealed class PumpSynchronizationContext
        : SynchronizationContext
    {
        private readonly ConcurrentQueue<(
            SendOrPostCallback Callback,
            object? State)> _callbacks = [];

        public override void Post(
            SendOrPostCallback callback,
            object? state) => _callbacks.Enqueue((callback, state));

        public T RunUntil<T>(Task<T> task)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (!task.IsCompleted)
            {
                if (_callbacks.TryDequeue(out var work))
                {
                    work.Callback(work.State);
                }
                else if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException(
                        "UI continuation did not complete.");
                }
                else
                {
                    Thread.Sleep(1);
                }
            }

            while (_callbacks.TryDequeue(out var remaining))
            {
                remaining.Callback(remaining.State);
            }

            return task.GetAwaiter().GetResult();
        }
    }
}
