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
    private string? _errorResourceId;
    private string? _recoveryResourceId;
    private string? _dataLoadWarningResourceId;
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
    
    public event Action<Guid>? UpdateCurrentRequested;

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
        string resourceId = exception switch
        {
            IOException or UnauthorizedAccessException =>
                "OverviewSaveError",
            OperationCanceledException =>
                "OverviewCanceledError",
            _ => "OverviewGenericError",
        };
        return _uiDispatcher.InvokeAsync(
            () => ShowErrorResource(resourceId),
            cancellationToken);
    }

    public Task ShowNotificationTargetMissingAsync(
        CancellationToken cancellationToken = default) =>
        _uiDispatcher.InvokeAsync(
            () => ShowErrorResource("OverviewNotificationTargetMissingError"),
            cancellationToken);

    public async Task<bool> ReorderGameAsync(
        Guid sourceGameId,
        Guid targetGameId,
        CancellationToken cancellationToken = default)
    {
        if (sourceGameId == targetGameId)
        {
            return false;
        }

        try
        {
            return await _gameManager.ReorderAsync(
                sourceGameId,
                targetGameId,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            await ShowErrorAsync(
                exception,
                CancellationToken.None);

            return false;
        }
    }

    internal void RefreshLocalizedText()
    {
        if (_errorResourceId is string errorResourceId)
        {
            ErrorMessage = _appResourceService.GetString(errorResourceId);
        }

        if (_recoveryResourceId is string recoveryResourceId)
        {
            RecoveryMessage = _appResourceService.GetString(recoveryResourceId);
        }

        if (_dataLoadWarningResourceId is string warningResourceId)
        {
            DataLoadWarningMessage = _appResourceService.GetString(
                warningResourceId);
        }
    }

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
                    _recoveryResourceId = null;
                    RecoveryMessage = string.Empty;
                    IsRecoveryInfoBarOpen = false;
                    return;
                }

                string resourceId = status.IsDiagnosticPreserved
                    ? "OverviewRecoveryDiagnosticPreserved"
                    : "OverviewRecoveryPromoted";
                _recoveryResourceId = resourceId;
                RecoveryMessage = _appResourceService.GetString(resourceId);
                IsRecoveryInfoBarOpen = true;
            },
            cancellationToken);
    }

    public void CloseRecoveryInfoBar()
    {
        _recoveryResourceId = null;
        IsRecoveryInfoBarOpen = false;
    }

    public Task ShowDataLoadWarningAsync(
        DataLoadWarning warning,
        CancellationToken cancellationToken = default) =>
        _uiDispatcher.InvokeAsync(
            () =>
            {
                if (warning != DataLoadWarning.SchemaMigrationWritebackFailed)
                {
                    _dataLoadWarningResourceId = null;
                    DataLoadWarningMessage = string.Empty;
                    IsDataLoadWarningInfoBarOpen = false;
                    return;
                }

                string resourceId = "OverviewSchemaWritebackWarning";
                _dataLoadWarningResourceId = resourceId;
                DataLoadWarningMessage = _appResourceService.GetString(
                    resourceId);
                IsDataLoadWarningInfoBarOpen = true;
            },
            cancellationToken);

    public void CloseDataLoadWarningInfoBar()
    {
        _dataLoadWarningResourceId = null;
        IsDataLoadWarningInfoBarOpen = false;
    }

    public Task ClearErrorAsync(
        CancellationToken cancellationToken = default) =>
        _uiDispatcher.InvokeAsync(
            () =>
            {
                _errorResourceId = null;
                ErrorMessage = null;
            },
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

    private void ShowErrorResource(string resourceId)
    {
        _errorResourceId = resourceId;
        ErrorMessage = _appResourceService.GetString(resourceId);
    }

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
                    id => EditGameRequested?.Invoke(id),
                    id => UpdateCurrentRequested?.Invoke(id));
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
