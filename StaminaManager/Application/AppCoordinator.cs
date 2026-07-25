using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;

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
    private readonly SemaphoreSlim _initializationGate = new(1, 1);

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
    {
        ArgumentNullException.ThrowIfNull(dataStore);
        ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(backdropService);
        _dataStore = dataStore;
        _gameManager = gameManager;
        _uiDispatcher = uiDispatcher;
        _themeService = themeService;
        _backdropService = backdropService;
    }

    public event EventHandler<AppNavigationRequest>? NavigationRequested;

    public DataLoadResult? LastLoadResult { get; private set; }

    public ThemeResult? LastThemeResult { get; private set; }

    public BackdropResult? LastBackdropResult { get; private set; }

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

            await RestoreModeAsync(cancellationToken)
                .ConfigureAwait(false);
            await ReconcileDerivedStateAsync(cancellationToken)
                .ConfigureAwait(false);
            IsInitialized = true;
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

    public Task ReconcileDerivedStateAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _uiDispatcher.InvokeAsync(
            () =>
            {
                AppSettings settings = _gameManager.CurrentData.Settings;
                LastThemeResult = _themeService.Apply(settings.Theme);
                LastBackdropResult = _backdropService.Apply(
                    settings.Backdrop);
            },
            cancellationToken);
    }

    private Task RaiseNavigationAsync(
        AppNavigationRequest request,
        CancellationToken cancellationToken) =>
        _uiDispatcher.InvokeAsync(
            () => NavigationRequested?.Invoke(this, request),
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
}
