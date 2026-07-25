using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using System.Collections.Immutable;

namespace StaminaManager.Application;

public sealed class GameManager
{
    private const string GameLimitMessage =
        "登録できるゲームは100件までです。";
    private readonly ILocalDataStore _dataStore;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private DataEnvelope _data;

    public GameManager(
        ILocalDataStore dataStore,
        IClock clock,
        AppSettings initialSettings)
    {
        ArgumentNullException.ThrowIfNull(dataStore);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(initialSettings);

        _dataStore = dataStore;
        _clock = clock;
        _data = new DataEnvelope(
            DataEnvelope.CurrentSchemaVersion,
            ImmutableArray<GameEntry>.Empty,
            initialSettings);
    }

    public event EventHandler? GamesChanged;

    public ImmutableArray<GameEntry> Games => _data.Games;

    public DataEnvelope CurrentData => _data;

    public void Initialize(DataEnvelope data)
    {
        ArgumentNullException.ThrowIfNull(data);
        ImmutableArray<GameEntry> normalized = NormalizeOrder(data.Games);
        _data = data with { Games = normalized };
        GamesChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<GameEntry> AddAsync(
        GameDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (_data.Games.Length >= GameEntryValidator.MaxGameCount)
            {
                throw new InvalidOperationException(GameLimitMessage);
            }

            DateTimeOffset recordedAtUtc = _clock.UtcNow.ToUniversalTime();
            EnsureValid(draft, recordedAtUtc);
            GameEntry entry = new(
                Guid.NewGuid(),
                draft.Name,
                draft.CurrentStamina,
                draft.MaxStamina,
                draft.RecoveryMinutes,
                recordedAtUtc,
                draft.ImageAssetId,
                _data.Games.Length);
            DataEnvelope candidate = _data with
            {
                Games = _data.Games.Add(entry),
            };

            await SaveAndPublishAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
            return entry;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<GameEntry> EditAsync(
        Guid gameId,
        GameDraft initialDraft,
        GameDraft editedDraft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(initialDraft);
        ArgumentNullException.ThrowIfNull(editedDraft);
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            int index = FindIndex(gameId);
            if (index < 0)
            {
                throw new KeyNotFoundException(
                    "編集対象のゲームが見つかりません。");
            }

            DateTimeOffset savedAtUtc = _clock.UtcNow.ToUniversalTime();
            GameEntry edited = GameEditPolicy.Apply(
                _data.Games[index],
                initialDraft,
                editedDraft,
                savedAtUtc);
            EnsureValid(
                new GameDraft(
                    edited.Name,
                    edited.BaseStamina,
                    edited.MaxStamina,
                    edited.RecoveryMinutes,
                    edited.ImageAssetId),
                edited.RecordedAtUtc);
            DataEnvelope candidate = _data with
            {
                Games = _data.Games.SetItem(index, edited),
            };

            await SaveAndPublishAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
            return edited;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<bool> DeleteAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            int index = FindIndex(gameId);
            if (index < 0)
            {
                return false;
            }

            ImmutableArray<GameEntry> remaining =
                _data.Games.RemoveAt(index);
            DataEnvelope candidate = _data with
            {
                Games = NormalizeOrder(remaining),
            };
            await SaveAndPublishAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private static void EnsureValid(
        GameDraft draft,
        DateTimeOffset recordedAtUtc)
    {
        ValidationResult validation = GameEntryValidator.Validate(
            draft,
            recordedAtUtc);
        if (!validation.IsValid)
        {
            throw new ArgumentException(
                "ゲームの入力値が正しくありません。",
                nameof(draft));
        }
    }

    private static ImmutableArray<GameEntry> NormalizeOrder(
        ImmutableArray<GameEntry> games) => games
        .OrderBy(game => game.SortOrder)
        .Select((game, index) => game with { SortOrder = index })
        .ToImmutableArray();

    private int FindIndex(Guid gameId)
    {
        for (int index = 0; index < _data.Games.Length; index++)
        {
            if (_data.Games[index].Id == gameId)
            {
                return index;
            }
        }

        return -1;
    }

    private async Task SaveAndPublishAsync(
        DataEnvelope candidate,
        CancellationToken cancellationToken)
    {
        await _dataStore.SaveAsync(candidate, cancellationToken)
            .ConfigureAwait(false);
        _data = candidate;
        GamesChanged?.Invoke(this, EventArgs.Empty);
    }
}
