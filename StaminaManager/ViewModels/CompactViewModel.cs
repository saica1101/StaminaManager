using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;

namespace StaminaManager.ViewModels;

public sealed partial class CompactViewModel : ObservableObject, IDisposable
{
    private readonly GameManager _gameManager;
    private readonly IClock _clock;
    private readonly AppCoordinator _coordinator;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly ObservableCollection<GameCardViewModel> _games = [];
    private bool _isDisposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedGame))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyCanExecuteChangedFor(nameof(UpdateCurrentCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditGameCommand))]
    public partial GameCardViewModel? SelectedGame { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddGameCommand))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    public partial string? ErrorMessage { get; private set; }

    public CompactViewModel(
        GameManager gameManager,
        IClock clock,
        AppCoordinator coordinator,
        IUiDispatcher uiDispatcher)
    {
        ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        _gameManager = gameManager;
        _clock = clock;
        _coordinator = coordinator;
        _uiDispatcher = uiDispatcher;
        Games = new ReadOnlyObservableCollection<GameCardViewModel>(
            _games);
        _gameManager.GamesChanged += OnGamesChanged;
        SynchronizeGamesCore(
            _gameManager.CurrentData.Settings.SelectedCompactGameId);
    }

    public event Action? AddGameRequested;

    public event Action<Guid, bool>? EditGameRequested;

    public event Action? ReturnOverviewRequested;

    public ReadOnlyObservableCollection<GameCardViewModel> Games { get; }

    public bool HasSelectedGame => SelectedGame is not null;

    public bool IsEmpty => SelectedGame is null;

    public void ShowError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ErrorMessage = message;
    }

    public string SelectedName => SelectedGame?.Name ?? string.Empty;

    public int SelectedCurrent => SelectedGame?.CurrentStamina ?? 0;

    public int SelectedMaximum => SelectedGame?.MaxStamina ?? 1;

    public double SelectedProgress => SelectedGame?.Progress ?? 0d;

    public StaminaStatus SelectedStatus =>
        SelectedGame?.Status ?? StaminaStatus.Safe;

    public string SelectedStatusText => SelectedStatus switch
    {
        StaminaStatus.Safe => "余裕",
        StaminaStatus.Attention => "注意",
        StaminaStatus.NearFull => "満タン間近",
        StaminaStatus.Full => "満タン",
        StaminaStatus.OverCap => "自然回復停止中",
        _ => throw new ArgumentOutOfRangeException(),
    };

    public string SelectedRemainingText
    {
        get
        {
            if (SelectedGame is null)
            {
                return string.Empty;
            }

            RemainingTimeParts parts = RemainingTimeParts.From(
                SelectedGame.Remaining);
            if (parts.IsFull)
            {
                return "満タン";
            }

            return parts.Days > 0
                ? string.Format(
                    CultureInfo.CurrentCulture,
                    "満タンまで {0}日 {1:00}:{2:00}",
                    parts.Days,
                    parts.Hours,
                    parts.Minutes)
                : string.Format(
                    CultureInfo.CurrentCulture,
                    "満タンまで {0:00}:{1:00}",
                    parts.Hours,
                    parts.Minutes);
        }
    }

    public async Task SelectGameAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        GameCardViewModel target = _games.FirstOrDefault(
                game => game.Id == gameId)
            ?? throw new KeyNotFoundException(
                "選択するゲームが見つかりません。");
        if (SelectedGame?.Id == target.Id)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _coordinator.SelectCompactGameAsync(
                    gameId,
                    cancellationToken)
                .ConfigureAwait(false);
            await _uiDispatcher.InvokeAsync(
                    () => SelectedGame = target,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            await _uiDispatcher.InvokeAsync(
                    () => ErrorMessage =
                        "選択を保存できませんでした。もう一度お試しください。",
                    CancellationToken.None)
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            await _uiDispatcher.InvokeAsync(
                    () => IsBusy = false,
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    public void ApplySelectedGame(Guid? gameId) =>
        SelectGameCore(ResolveGameId(gameId));

    public void RefreshOnUiThread(DateTimeOffset nowUtc)
    {
        foreach (GameCardViewModel game in _games)
        {
            game.Refresh(nowUtc);
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _gameManager.GamesChanged -= OnGamesChanged;
        _isDisposed = true;
    }

    [RelayCommand(CanExecute = nameof(CanAddGame))]
    private void AddGame() => AddGameRequested?.Invoke();

    [RelayCommand(CanExecute = nameof(HasGameSelected))]
    private void UpdateCurrent()
    {
        if (SelectedGame is not null)
        {
            EditGameRequested?.Invoke(
                SelectedGame.Id,
                true);
        }
    }

    [RelayCommand(CanExecute = nameof(HasGameSelected))]
    private void EditGame()
    {
        if (SelectedGame is not null)
        {
            EditGameRequested?.Invoke(
                SelectedGame.Id,
                false);
        }
    }

    [RelayCommand]
    private void ReturnOverview() => ReturnOverviewRequested?.Invoke();

    private bool CanAddGame() => !IsBusy;

    private bool HasGameSelected() =>
        SelectedGame is not null && !IsBusy;

    private async Task OnGamesChanged(
        CancellationToken cancellationToken)
    {
        Guid? previousId = SelectedGame?.Id;
        Guid? fallbackId = null;
        await _uiDispatcher.InvokeAsync(
                () =>
                {
                    SynchronizeGamesCore(previousId);
                    fallbackId = SelectedGame?.Id;
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (_coordinator.IsInitialized
            && _gameManager.CurrentData.Settings.SelectedCompactGameId
                != fallbackId)
        {
            await _coordinator.SelectCompactGameAsync(
                    fallbackId,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private void SynchronizeGamesCore(Guid? preferredGameId)
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
                card.Refresh(entry, _clock.UtcNow);
            }
            else
            {
                card = new GameCardViewModel(entry, _clock.UtcNow);
            }

            _games.Add(card);
        }

        SelectGameCore(ResolveGameId(preferredGameId));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(HasSelectedGame));
    }

    private Guid? ResolveGameId(Guid? preferredGameId)
    {
        if (preferredGameId is Guid id
            && _games.Any(game => game.Id == id))
        {
            return id;
        }

        return _games.Count == 0 ? null : _games[0].Id;
    }

    private void SelectGameCore(Guid? gameId)
    {
        GameCardViewModel? selected = gameId is Guid id
            ? _games.FirstOrDefault(game => game.Id == id)
            : null;
        if (ReferenceEquals(SelectedGame, selected))
        {
            OnPropertyChanged(nameof(SelectedGame));
            return;
        }

        SelectedGame = selected;
    }

    partial void OnSelectedGameChanging(GameCardViewModel? value)
    {
        if (SelectedGame is not null)
        {
            SelectedGame.PropertyChanged -= OnSelectedGamePropertyChanged;
        }
    }

    partial void OnSelectedGameChanged(GameCardViewModel? value)
    {
        if (value is not null)
        {
            value.PropertyChanged += OnSelectedGamePropertyChanged;
        }

        NotifySelectedPresentationChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        AddGameCommand.NotifyCanExecuteChanged();
        UpdateCurrentCommand.NotifyCanExecuteChanged();
        EditGameCommand.NotifyCanExecuteChanged();
    }

    private void OnSelectedGamePropertyChanged(
        object? sender,
        PropertyChangedEventArgs args) =>
        NotifySelectedPresentationChanged();

    private void NotifySelectedPresentationChanged()
    {
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(SelectedCurrent));
        OnPropertyChanged(nameof(SelectedMaximum));
        OnPropertyChanged(nameof(SelectedProgress));
        OnPropertyChanged(nameof(SelectedStatus));
        OnPropertyChanged(nameof(SelectedStatusText));
        OnPropertyChanged(nameof(SelectedRemainingText));
    }
}
