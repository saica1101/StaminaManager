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

    public AppCoordinator(
        ILocalDataStore dataStore,
        GameManager gameManager)
    {
        ArgumentNullException.ThrowIfNull(dataStore);
        ArgumentNullException.ThrowIfNull(gameManager);
        _dataStore = dataStore;
        _gameManager = gameManager;
    }

    public event EventHandler<AppNavigationRequest>? NavigationRequested;

    public DataLoadResult? LastLoadResult { get; private set; }

    public bool IsInitialized { get; private set; }

    public async Task<DataLoadResult> InitializeAsync(
        CancellationToken cancellationToken)
    {
        DataLoadResult result = await _dataStore.LoadAsync(
            cancellationToken).ConfigureAwait(false);
        if (result.Envelope is not null)
        {
            _gameManager.Initialize(result.Envelope);
        }

        LastLoadResult = result;
        IsInitialized = true;
        RestoreMode();
        await ReconcileDerivedStateAsync(cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    public async Task<RecoveryPromotionResult> PromoteRecoveryAsync(
        CancellationToken cancellationToken)
    {
        RecoveryPromotionResult result =
            await _dataStore.PromoteRecoveryAsync(cancellationToken)
                .ConfigureAwait(false);
        _gameManager.Initialize(result.Envelope);
        LastLoadResult = new DataLoadResult(
            DataLoadStatus.Primary,
            result.Envelope,
            result.PrimaryPath,
            result.RecoveryPath);
        await ReconcileDerivedStateAsync(cancellationToken)
            .ConfigureAwait(false);
        return result;
    }

    public void RouteActivation(Guid? gameId)
    {
        NavigationRequested?.Invoke(
            this,
            new AppNavigationRequest(
                AppPage.Overview,
                AppDisplayMode.Standard,
                gameId));
    }

    public void RestoreMode()
    {
        NavigationRequested?.Invoke(
            this,
            new AppNavigationRequest(
                AppPage.Overview,
                AppDisplayMode.Standard,
                GameId: null));
    }

    public Task ReconcileDerivedStateAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
