using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Persistence;

namespace StaminaManager.Application;

public enum AppPage
{
    Overview,
    Settings,
}

public enum AppDisplayMode
{
    Standard,
    Compact,
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
    private readonly SemaphoreSlim _initializationGate = new(1, 1);

    public AppCoordinator(
        ILocalDataStore dataStore,
        GameManager gameManager,
        IUiDispatcher uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(dataStore);
        ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        _dataStore = dataStore;
        _gameManager = gameManager;
        _uiDispatcher = uiDispatcher;
    }

    public event EventHandler<AppNavigationRequest>? NavigationRequested;

    public DataLoadResult? LastLoadResult { get; private set; }

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

    public Task RestoreModeAsync(
        CancellationToken cancellationToken = default) =>
        RaiseNavigationAsync(
            new AppNavigationRequest(
                AppPage.Overview,
                AppDisplayMode.Standard,
                GameId: null),
            cancellationToken);

    public Task ReconcileDerivedStateAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    private Task RaiseNavigationAsync(
        AppNavigationRequest request,
        CancellationToken cancellationToken) =>
        _uiDispatcher.InvokeAsync(
            () => NavigationRequested?.Invoke(this, request),
            cancellationToken);
}
