using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using System.Collections.ObjectModel;

namespace StaminaManager.ViewModels;

public sealed partial class OverviewViewModel : ObservableObject, IDisposable
{
    private readonly GameManager _gameManager;
    private readonly IClock _clock;
    private readonly ObservableCollection<GameCardViewModel> _games = [];
    private bool _isDisposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasGames))]
    public partial bool IsLoading { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? ErrorMessage { get; set; }

    public OverviewViewModel(
        GameManager gameManager,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(clock);
        _gameManager = gameManager;
        _clock = clock;
        Games = new ReadOnlyObservableCollection<GameCardViewModel>(
            _games);
        _gameManager.GamesChanged += OnGamesChanged;
        SynchronizeGames(_clock.UtcNow);
    }

    public event Action? AddGameRequested;

    public event Action<Guid>? EditGameRequested;

    public event Action? CompactModeRequested;

    public ReadOnlyObservableCollection<GameCardViewModel> Games { get; }

    public bool HasGames => !IsLoading && _games.Count > 0;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public void Refresh(DateTimeOffset nowUtc)
    {
        foreach (GameCardViewModel game in _games)
        {
            game.Refresh(nowUtc);
        }
    }

    public void ShowError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ErrorMessage = exception.Message;
    }

    public void ClearError() => ErrorMessage = null;

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _gameManager.GamesChanged -= OnGamesChanged;
        _isDisposed = true;
    }

    [RelayCommand]
    private void AddGame() => AddGameRequested?.Invoke();

    [RelayCommand]
    private void EnterCompactMode() => CompactModeRequested?.Invoke();

    private void OnGamesChanged(object? sender, EventArgs eventArgs) =>
        SynchronizeGames(_clock.UtcNow);

    private void SynchronizeGames(DateTimeOffset nowUtc)
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

        OnPropertyChanged(nameof(HasGames));
    }
}
