using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using System.Diagnostics;

namespace StaminaManager.Application;

public enum AppPage
{
    Overview,
    Settings,
}

public sealed record AppNavigationRequest(
    AppPage Page,
    AppDisplayMode DisplayMode,
    Guid? GameId);

public sealed class AppCoordinator
{
    private readonly ILocalDataStore _dataStore;
    private readonly GameManager _gameManager;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IThemeService _themeService;
    private readonly IBackdropService _backdropService;
    private readonly IStartupService _startupService;
    private readonly IWindowStateService _windowStateService;
    private readonly INotificationReconciler _notificationReconciler;
    private readonly RestoreCoordinator? _restoreCoordinator;
    private readonly SemaphoreSlim _initializationGate = new(1, 1);
    private volatile bool _isInteractiveRestoreInProgress;

    public AppCoordinator(
        ILocalDataStore dataStore,
        GameManager gameManager,
        IUiDispatcher uiDispatcher)
        : this(
            dataStore,
            gameManager,
            uiDispatcher,
            new PassThroughThemeService(),
            new PassThroughBackdropService())
    {
    }

    public AppCoordinator(
        ILocalDataStore dataStore,
        GameManager gameManager,
        IUiDispatcher uiDispatcher,
        IThemeService themeService,
        IBackdropService backdropService)
        : this(
            dataStore,
            gameManager,
            uiDispatcher,
            themeService,
            backdropService,
            new PassThroughStartupService(
                () => gameManager.CurrentData.Settings.StartupEnabled))
    {
    }

    public AppCoordinator(
        ILocalDataStore dataStore,
        GameManager gameManager,
        IUiDispatcher uiDispatcher,
        IThemeService themeService,
        IBackdropService backdropService,
        IStartupService startupService)
        : this(
            dataStore,
            gameManager,
            uiDispatcher,
            themeService,
            backdropService,
            startupService,
            new PassThroughWindowStateService(),
            new PassThroughNotificationReconciler())
    {
    }

    public AppCoordinator(
        ILocalDataStore dataStore,
        GameManager gameManager,
        IUiDispatcher uiDispatcher,
        IThemeService themeService,
        IBackdropService backdropService,
        IStartupService startupService,
        IWindowStateService windowStateService)
        : this(
            dataStore,
            gameManager,
            uiDispatcher,
            themeService,
            backdropService,
            startupService,
            windowStateService,
            new PassThroughNotificationReconciler())
    {
    }

    public AppCoordinator(
        ILocalDataStore dataStore,
        GameManager gameManager,
        IUiDispatcher uiDispatcher,
        IThemeService themeService,
        IBackdropService backdropService,
        IStartupService startupService,
        IWindowStateService windowStateService,
        INotificationReconciler notificationReconciler,
        RestoreCoordinator? restoreCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(dataStore);
        ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(backdropService);
        ArgumentNullException.ThrowIfNull(startupService);
        ArgumentNullException.ThrowIfNull(windowStateService);
        ArgumentNullException.ThrowIfNull(notificationReconciler);
        _dataStore = dataStore;
        _gameManager = gameManager;
        _uiDispatcher = uiDispatcher;
        _themeService = themeService;
        _backdropService = backdropService;
        _startupService = startupService;
        _windowStateService = windowStateService;
        _notificationReconciler = notificationReconciler;
        _restoreCoordinator = restoreCoordinator;
    }

    public event EventHandler<AppNavigationRequest>? NavigationRequested;

    public DataLoadResult? LastLoadResult { get; private set; }

    public ThemeResult? LastThemeResult { get; private set; }

    public BackdropResult? LastBackdropResult { get; private set; }

    public StartupStatus? LastStartupStatus { get; private set; }

    public NotificationReconcileResult? LastNotificationReconcileResult
    {
        get;
        private set;
    }

    public bool IsStartupSynchronized { get; private set; } = true;

    public StartupFailureReason StartupReconcileFailureReason
    {
        get;
        private set;
    } = StartupFailureReason.None;

    public bool IsInitialized { get; private set; }

    public async Task<DataLoadResult> InitializeAsync(
        CancellationToken cancellationToken)
    {
        await _initializationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "アプリケーションは既に初期化されています。");
            }

            DataLoadResult result;
            if (!_gameManager.IsInitialized)
            {
                result = await _dataStore.LoadAsync(cancellationToken)
                    .ConfigureAwait(false);
                DataEnvelope? initialData = result.Envelope;
                if (result.Status == DataLoadStatus.Empty)
                {
                    initialData = _gameManager.CurrentData;
                }

                if (initialData is not null)
                {
                    await _gameManager.InitializeAsync(
                        initialData,
                        cancellationToken).ConfigureAwait(false);
                }
                else if (result.Status != DataLoadStatus.Corrupt)
                {
                    throw new InvalidDataException(
                        "読み込み結果にアプリデータがありません。");
                }

                LastLoadResult = result;
            }
            else
            {
                result = LastLoadResult ?? throw new InvalidOperationException(
                    "初期化状態を復元できません。");
            }

            BackupRestoreResult? resumedRestore = null;
            if (_restoreCoordinator is not null)
            {
                resumedRestore = await _restoreCoordinator.ResumeAsync(
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (resumedRestore is null)
            {
                await ReconcileDerivedStateAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await ReconcileRestoredDerivedStateAsync(
                        resumedRestore.Data.Settings.StartupEnabled,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            await AcknowledgeRestoreIfReconciledAsync(cancellationToken)
                .ConfigureAwait(false);
            IsInitialized = true;
            _gameManager.GamesChanged += OnGamesChangedAsync;
            return result;
        }
        finally
        {
            _initializationGate.Release();
        }
    }

    public async Task<RecoveryPromotionResult> PromoteRecoveryAsync(
        CancellationToken cancellationToken)
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException(
                "初期化が完了するまで回復データを適用できません。");
        }

        RecoveryPromotionResult result =
            await _gameManager.PromoteRecoveryAsync(cancellationToken)
                .ConfigureAwait(false);
        LastLoadResult = new DataLoadResult(
            DataLoadStatus.Primary,
            result.Envelope,
            result.PrimaryPath,
            result.RecoveryPath);
        await ReconcileDerivedStateAsync(cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    public Task RouteActivationAsync(
        Guid? gameId,
        CancellationToken cancellationToken = default) =>
        RaiseNavigationAsync(
            new AppNavigationRequest(
                AppPage.Overview,
                AppDisplayMode.Standard,
                gameId),
            cancellationToken);

    public async Task RestoreModeAsync(
        CancellationToken cancellationToken = default)
    {
        AppSettings settings = _gameManager.CurrentData.Settings;
        Guid? selectedGameId = ResolveCompactGameId(
            settings.SelectedCompactGameId);
        if (settings.SelectedCompactGameId != selectedGameId)
        {
            settings = await _gameManager.UpdateSettingsAsync(
                    current => current with
                    {
                        SelectedCompactGameId = selectedGameId,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
        }

        await RaiseNavigationAsync(
                new AppNavigationRequest(
                    AppPage.Overview,
                    settings.LastDisplayMode,
                    selectedGameId),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ChangeDisplayModeAsync(
        AppDisplayMode displayMode,
        Guid? requestedGameId,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        Guid? selectedGameId = displayMode == AppDisplayMode.Compact
            ? ResolveCompactGameId(requestedGameId)
            : ResolveCompactGameId(
                _gameManager.CurrentData.Settings.SelectedCompactGameId);
        AppSettings settings = await _gameManager.UpdateSettingsAsync(
                current => current with
                {
                    LastDisplayMode = displayMode,
                    SelectedCompactGameId = selectedGameId,
                },
                cancellationToken)
            .ConfigureAwait(false);
        await RaiseNavigationAsync(
                new AppNavigationRequest(
                    AppPage.Overview,
                    settings.LastDisplayMode,
                    displayMode == AppDisplayMode.Compact
                        ? selectedGameId
                        : requestedGameId),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task SelectCompactGameAsync(
        Guid? requestedGameId,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        Guid? selectedGameId = ResolveCompactGameId(requestedGameId);
        AppSettings settings = await _gameManager.UpdateSettingsAsync(
                current => current with
                {
                    SelectedCompactGameId = selectedGameId,
                },
                cancellationToken)
            .ConfigureAwait(false);
        await RaiseNavigationAsync(
                new AppNavigationRequest(
                    AppPage.Overview,
                    settings.LastDisplayMode,
                    selectedGameId),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ReconcileDerivedStateAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _uiDispatcher.InvokeAsync(
            () =>
            {
                AppSettings settings = _gameManager.CurrentData.Settings;
                LastThemeResult = _themeService.Apply(settings.Theme);
                LastBackdropResult = _backdropService.Apply(
                    settings.Backdrop);
            },
            cancellationToken).ConfigureAwait(false);

        if (!_gameManager.IsInitialized)
        {
            return;
        }

        await RestoreModeAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await ReconcileStartupAsync(cancellationToken).ConfigureAwait(false);
        await ReconcileNotificationsAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task ExportBackupAsync(
        string destinationPath,
        CancellationToken cancellationToken = default) =>
        GetRestoreCoordinator().ExportAsync(
            destinationPath,
            cancellationToken);

    public Task<PreparedBackupRestore> PreviewRestoreAsync(
        string sourcePath,
        CancellationToken cancellationToken = default) =>
        GetRestoreCoordinator().PrepareAsync(sourcePath, cancellationToken);

    public Task CancelPreparedRestoreAsync(
        string sessionId,
        CancellationToken cancellationToken = default) =>
        GetRestoreCoordinator().CancelPreparedAsync(
            sessionId,
            cancellationToken);

    public async Task<BackupRestoreResult> RestoreBackupAsync(
        string sessionId,
        bool isReplacementConfirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureInitialized();
        _isInteractiveRestoreInProgress = true;
        try
        {
            BackupRestoreResult result = await GetRestoreCoordinator()
                .CommitPreparedAsync(
                    sessionId,
                    isReplacementConfirmed,
                    cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await ReconcileRestoredDerivedStateAsync(
                        result.Data.Settings.StartupEnabled,
                        cancellationToken)
                    .ConfigureAwait(false);
                bool acknowledged =
                    await AcknowledgeRestoreIfReconciledAsync(
                            cancellationToken)
                        .ConfigureAwait(false);
                return result with
                {
                    IsPartial = !acknowledged,
                    RequiresDerivedStateRetry = !acknowledged,
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                Debug.WriteLine(
                    "Post-commit restore reconciliation failed: "
                    + exception.GetType().Name);
                return result with
                {
                    IsPartial = true,
                    RequiresDerivedStateRetry = true,
                };
            }
        }
        finally
        {
            _isInteractiveRestoreInProgress = false;
        }
    }

    private async Task ReconcileRestoredDerivedStateAsync(
        bool desiredStartupEnabled,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _uiDispatcher.InvokeAsync(
            () =>
            {
                AppSettings settings = _gameManager.CurrentData.Settings;
                LastThemeResult = _themeService.Apply(settings.Theme);
                LastBackdropResult = _backdropService.Apply(
                    settings.Backdrop);
            },
            cancellationToken).ConfigureAwait(false);
        await RestoreModeAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await ApplyRestoredStartupAsync(
                desiredStartupEnabled,
                cancellationToken)
            .ConfigureAwait(false);
        await ReconcileNotificationsAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ApplyRestoredStartupAsync(
        bool desiredStartupEnabled,
        CancellationToken cancellationToken)
    {
        IsStartupSynchronized = false;
        StartupReconcileFailureReason = StartupFailureReason.OperationFailed;
        StartupChangeResult change;
        try
        {
            change = await _startupService.SetEnabledAsync(
                    desiredStartupEnabled,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Restored StartupTask apply failed: "
                + exception.GetType().Name);
            return;
        }

        LastStartupStatus = change.Status;
        bool actualEnabled = change.Status.IsEnabled;
        if (_gameManager.CurrentData.Settings.StartupEnabled
            != actualEnabled)
        {
            try
            {
                await _gameManager.UpdateSettingsAsync(
                        settings => settings with
                        {
                            StartupEnabled = actualEnabled,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                Debug.WriteLine(
                    "Restored StartupTask correction save failed: "
                    + exception.GetType().Name);
                return;
            }
        }

        if (change.IsApplied && actualEnabled == desiredStartupEnabled)
        {
            IsStartupSynchronized = true;
            StartupReconcileFailureReason = StartupFailureReason.None;
            return;
        }

        StartupReconcileFailureReason = change.FailureReason
            == StartupFailureReason.None
                ? StartupFailureReason.OperationFailed
                : change.FailureReason;
    }

    private async Task<bool> AcknowledgeRestoreIfReconciledAsync(
        CancellationToken cancellationToken)
    {
        if (_restoreCoordinator is null
            || !IsStartupSynchronized
            || LastThemeResult?.IsApplied != true
            || LastBackdropResult?.ErrorMessage is not null
            || LastNotificationReconcileResult?.HasFailures == true)
        {
            return false;
        }

        try
        {
            await _restoreCoordinator.AcknowledgeDerivedStateAsync(
                cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Restore acknowledgement failed: "
                + exception.GetType().Name);
            return false;
        }
    }

    private RestoreCoordinator GetRestoreCoordinator() =>
        _restoreCoordinator ?? throw new InvalidOperationException(
            "バックアップ機能が登録されていません。");

    private async Task ReconcileStartupAsync(
        CancellationToken cancellationToken)
    {
        IsStartupSynchronized = false;
        StartupReconcileFailureReason = StartupFailureReason.OperationFailed;
        StartupStatus startupStatus;
        try
        {
            startupStatus = await _startupService.GetStatusAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "StartupTask reconciliation failed: "
                + exception.GetType().Name);
            return;
        }

        LastStartupStatus = startupStatus;
        AppSettings currentSettings = _gameManager.CurrentData.Settings;
        if (currentSettings.StartupEnabled == startupStatus.IsEnabled)
        {
            IsStartupSynchronized = true;
            StartupReconcileFailureReason = StartupFailureReason.None;
            return;
        }

        try
        {
            await _gameManager.UpdateSettingsAsync(
                    settings => settings with
                    {
                        StartupEnabled = startupStatus.IsEnabled,
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            IsStartupSynchronized = true;
            StartupReconcileFailureReason = StartupFailureReason.None;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "StartupTask reconciliation save failed: "
                + exception.GetType().Name);
        }
    }

    private async Task ReconcileNotificationsAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            LastNotificationReconcileResult =
                await _notificationReconciler.ReconcileAsync(
                    _gameManager.Games,
                    _gameManager.CurrentData.Settings,
                    cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Notification reconciliation failed: "
                + exception.GetType().Name);
            LastNotificationReconcileResult = new NotificationReconcileResult(
                System.Collections.Immutable.ImmutableArray.Create(
                    new NotificationReconcileIssue(
                        GameId: null,
                        NotificationDecisionError.None,
                        exception.GetType().Name)));
        }
    }

    private async Task OnGamesChangedAsync(
        CancellationToken cancellationToken)
    {
        if (!IsInitialized || _isInteractiveRestoreInProgress)
        {
            return;
        }

        await ReconcileNotificationsAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private Task RaiseNavigationAsync(
        AppNavigationRequest request,
        CancellationToken cancellationToken) =>
        _uiDispatcher.InvokeAsync(
            () =>
            {
                _windowStateService.ApplyDisplayMode(request.DisplayMode);
                NavigationRequested?.Invoke(this, request);
            },
            cancellationToken);

    private Guid? ResolveCompactGameId(Guid? requestedGameId)
    {
        if (requestedGameId is Guid id
            && _gameManager.Games.Any(game => game.Id == id))
        {
            return id;
        }

        return _gameManager.Games.IsEmpty
            ? null
            : _gameManager.Games[0].Id;
    }

    private void EnsureInitialized()
    {
        if (!IsInitialized)
        {
            throw new InvalidOperationException(
                "初期化が完了するまで表示モードを変更できません。");
        }
    }

    private static bool IsProcessFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;

    private sealed class PassThroughThemeService : IThemeService
    {
        public AppTheme ResolveInitialTheme() => AppTheme.Light;

        public ThemeResult Apply(AppTheme requestedTheme) => new(
            requestedTheme,
            requestedTheme,
            IsApplied: true,
            ErrorMessage: null);
    }

    private sealed class PassThroughBackdropService : IBackdropService
    {
        public BackdropResult Apply(BackdropKind requestedBackdrop) => new(
            requestedBackdrop,
            requestedBackdrop,
            BackdropFallbackReason.None,
            ErrorMessage: null);
    }

    private sealed class PassThroughStartupService(
        Func<bool> getSavedState) : IStartupService
    {
        public Task<StartupStatus> GetStatusAsync(
            CancellationToken cancellationToken)
        {
            bool isEnabled = getSavedState();
            return Task.FromResult(new StartupStatus(
                isEnabled ? StartupState.Enabled : StartupState.Disabled));
        }

        public Task<StartupChangeResult> SetEnabledAsync(
            bool isEnabled,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class PassThroughWindowStateService : IWindowStateService
    {
        public AppDisplayMode CurrentDisplayMode { get; private set; } =
            AppDisplayMode.Standard;

        public void ApplyDisplayMode(AppDisplayMode displayMode) =>
            CurrentDisplayMode = displayMode;

        public void CaptureCurrent()
        {
        }
    }

    private sealed class PassThroughNotificationReconciler
        : INotificationReconciler
    {
        public Task<NotificationReconcileResult> ReconcileAsync(
            IReadOnlyCollection<GameEntry> games,
            AppSettings settings,
            CancellationToken cancellationToken) => Task.FromResult(
                NotificationReconcileResult.Success);
    }
}
