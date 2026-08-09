using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using System.Collections.ObjectModel;

namespace StaminaManager.ViewModels;

public sealed partial class OverviewViewModel : ObservableObject, IDisposable
{
    private readonly GameManager _gameManager;
    private readonly IClock _clock;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly IAppResourceService _appResourceService;
    private readonly ObservableCollection<GameCardViewModel> _games = [];
    private readonly ObservableCollection<OverviewItemViewModel>
        _overviewItems = [];
    private readonly AddGameItemViewModel _addGameItem;
    private bool _isDisposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGames))]
    [NotifyCanExecuteChangedFor(nameof(AddGameCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnterCompactModeCommand))]
    public partial bool IsLoading { get; private set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; private set; }

    [ObservableProperty]
    public partial bool IsRecoveryInfoBarOpen { get; private set; }

    [ObservableProperty]
    public partial string RecoveryMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsDataLoadWarningInfoBarOpen { get; private set; }

    [ObservableProperty]
    public partial string DataLoadWarningMessage { get; private set; } =
        string.Empty;

    public OverviewViewModel(
        GameManager gameManager,
        IClock clock,
        IUiDispatcher uiDispatcher,
        IAppResourceService appResourceService)
    {
        ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(appResourceService);
        _gameManager = gameManager;
        _clock = clock;
        _uiDispatcher = uiDispatcher;
        _appResourceService = appResourceService;
        Games = new ReadOnlyObservableCollection<GameCardViewModel>(
            _games);
        OverviewItems =
            new ReadOnlyObservableCollection<OverviewItemViewModel>(
                _overviewItems);
        _addGameItem = new AddGameItemViewModel(AddGameCommand);
        _gameManager.GamesChanged += OnGamesChanged;
        if (_gameManager.IsInitialized)
        {
            SynchronizeGamesCore(_clock.UtcNow);
        }
        else
        {
            RebuildOverviewItems();
        }
    }

    public event Action? AddGameRequested;

    public event Action<Guid>? EditGameRequested;

    public event Action? CompactModeRequested;

    public ReadOnlyObservableCollection<GameCardViewModel> Games { get; }

    public ReadOnlyObservableCollection<OverviewItemViewModel> OverviewItems
    { get; }

    public bool HasGames => !IsLoading && _games.Count > 0;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public void RefreshOnUiThread(DateTimeOffset nowUtc) =>
        RefreshCore(nowUtc);

    public Task SetLoadingAsync(
        bool isLoading,
        CancellationToken cancellationToken = default) =>
        _uiDispatcher.InvokeAsync(
            () => IsLoading = isLoading,
            cancellationToken);

    public Task ShowErrorAsync(
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exception);
        string safeMessage = exception switch
        {
            IOException or UnauthorizedAccessException =>
                _appResourceService.GetString("OverviewSaveError"),
            OperationCanceledException =>
                _appResourceService.GetString("OverviewCanceledError"),
            _ => _appResourceService.GetString("OverviewGenericError"),
        };
        return _uiDispatcher.InvokeAsync(
            () => ErrorMessage = safeMessage,
            cancellationToken);
    }

    public Task ShowNotificationTargetMissingAsync(
        CancellationToken cancellationToken = default) =>
        _uiDispatcher.InvokeAsync(
            () => ErrorMessage = _appResourceService.GetString(
                "OverviewNotificationTargetMissingError"),
            cancellationToken);

    public Task ShowStartupRecoveryAsync(
        StartupRecoveryStatus status,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);
        return _uiDispatcher.InvokeAsync(
            () =>
            {
                if (status.Kind != StartupRecoveryKind.Promoted)
                {
                    RecoveryMessage = string.Empty;
                    IsRecoveryInfoBarOpen = false;
                    return;
                }

                RecoveryMessage = status.IsDiagnosticPreserved
                    ? _appResourceService.GetString(
                        "OverviewRecoveryDiagnosticPreserved")
                    : _appResourceService.GetString(
                        "OverviewRecoveryPromoted");
                IsRecoveryInfoBarOpen = true;
            },
            cancellationToken);
    }

    public void CloseRecoveryInfoBar() => IsRecoveryInfoBarOpen = false;

    public Task ShowDataLoadWarningAsync(
        DataLoadWarning warning,
        CancellationToken cancellationToken = default) =>
        _uiDispatcher.InvokeAsync(
            () =>
            {
                if (warning != DataLoadWarning.SchemaMigrationWritebackFailed)
                {
                    DataLoadWarningMessage = string.Empty;
                    IsDataLoadWarningInfoBarOpen = false;
                    return;
                }

                DataLoadWarningMessage = _appResourceService.GetString(
                    "OverviewSchemaWritebackWarning");
                IsDataLoadWarningInfoBarOpen = true;
            },
            cancellationToken);

    public void CloseDataLoadWarningInfoBar() =>
        IsDataLoadWarningInfoBarOpen = false;

    public Task ClearErrorAsync(
        CancellationToken cancellationToken = default) =>
        _uiDispatcher.InvokeAsync(
            () => ErrorMessage = null,
            cancellationToken);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _gameManager.GamesChanged -= OnGamesChanged;
        _isDisposed = true;
    }

    [RelayCommand(CanExecute = nameof(CanRequestActions))]
    private void AddGame() => AddGameRequested?.Invoke();

    [RelayCommand(CanExecute = nameof(CanRequestActions))]
    private void EnterCompactMode() => CompactModeRequested?.Invoke();

    private bool CanRequestActions() => !IsLoading;

    private Task OnGamesChanged(CancellationToken cancellationToken) =>
        _uiDispatcher.InvokeAsync(
            () => SynchronizeGamesCore(_clock.UtcNow),
            cancellationToken);

    private void RefreshCore(DateTimeOffset nowUtc)
    {
        foreach (GameCardViewModel game in _games)
        {
            game.Refresh(nowUtc);
        }
    }

    private void SynchronizeGamesCore(DateTimeOffset nowUtc)
    {
        Dictionary<Guid, GameCardViewModel> existing = _games
            .ToDictionary(game => game.Id);
        _games.Clear();
        foreach (GameEntry entry in _gameManager.Games)
        {
            if (existing.TryGetValue(
                entry.Id,
                out GameCardViewModel? card))
            {
                card.Refresh(entry, nowUtc);
            }
            else
            {
                card = new GameCardViewModel(
                    entry,
                    nowUtc,
                    id => EditGameRequested?.Invoke(id));
            }

            _games.Add(card);
        }

        RebuildOverviewItems();

        OnPropertyChanged(nameof(HasGames));
    }

    private void RebuildOverviewItems()
    {
        _overviewItems.Clear();
        foreach (GameCardViewModel game in _games)
        {
            _overviewItems.Add(game);
        }

        _overviewItems.Add(_addGameItem);
    }
}
