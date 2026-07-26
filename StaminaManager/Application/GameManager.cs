using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using System.Collections.Immutable;

namespace StaminaManager.Application;

public delegate Task GamesChangedHandler(CancellationToken cancellationToken);

public sealed class GameManager
{
    private const string GameLimitMessage =
        "登録できるゲームは100件までです。";
    private readonly ILocalDataStore _dataStore;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private DataEnvelope _data;
    private volatile bool _isInitialized;

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

    public event GamesChangedHandler? GamesChanged;

    public Exception? LastNotificationError { get; private set; }

    public bool IsInitialized => _isInitialized;

    public ImmutableArray<GameEntry> Games => _data.Games;

    public DataEnvelope CurrentData => _data;

    public async Task InitializeAsync(
        DataEnvelope data,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                throw new InvalidOperationException(
                    "ゲーム管理は既に初期化されています。");
            }

            ImmutableArray<GameEntry> normalized =
                NormalizeOrder(data.Games);
            _data = data with { Games = normalized };
            _isInitialized = true;
        }
        finally
        {
            _mutationGate.Release();
        }

        await NotifyGamesChangedAsync().ConfigureAwait(false);
    }

    public async Task<GameEntry> AddAsync(
        GameDraft draft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        GameEntry entry;
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            if (_data.Games.Length >= GameEntryValidator.MaxGameCount)
            {
                throw new InvalidOperationException(GameLimitMessage);
            }

            DateTimeOffset recordedAtUtc = _clock.UtcNow.ToUniversalTime();
            EnsureValid(draft, recordedAtUtc);
            entry = new GameEntry(
                Guid.NewGuid(),
                draft.Name,
                draft.CurrentStamina,
                draft.MaxStamina,
                draft.RecoveryMinutes,
                recordedAtUtc,
                draft.ImageAssetId,
                _data.Games.Length);
            AppSettings settings =
                _data.Settings.SelectedCompactGameId is null
                    ? _data.Settings with
                    {
                        SelectedCompactGameId = _data.Games.IsEmpty
                            ? entry.Id
                            : _data.Games[0].Id,
                    }
                    : _data.Settings;
            DataEnvelope candidate = _data with
            {
                Games = _data.Games.Add(entry),
                Settings = settings,
            };

            await SaveAndPublishAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }

        await NotifyGamesChangedAsync().ConfigureAwait(false);
        return entry;
    }

    public async Task<GameEntry> EditAsync(
        Guid gameId,
        GameDraft initialDraft,
        GameDraft editedDraft,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(initialDraft);
        ArgumentNullException.ThrowIfNull(editedDraft);
        GameEntry edited;
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            int index = FindIndex(gameId);
            if (index < 0)
            {
                throw new KeyNotFoundException(
                    "編集対象のゲームが見つかりません。");
            }

            DateTimeOffset savedAtUtc = _clock.UtcNow.ToUniversalTime();
            edited = GameEditPolicy.Apply(
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
        }
        finally
        {
            _mutationGate.Release();
        }

        await NotifyGamesChangedAsync().ConfigureAwait(false);
        return edited;
    }

    public async Task<bool> DeleteAsync(
        Guid gameId,
        CancellationToken cancellationToken)
    {
        bool deleted = false;
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            int index = FindIndex(gameId);
            if (index < 0)
            {
                return false;
            }

            ImmutableArray<GameEntry> remaining =
                _data.Games.RemoveAt(index);
            ImmutableArray<GameEntry> normalized =
                NormalizeOrder(remaining);
            AppSettings settings =
                _data.Settings.SelectedCompactGameId == gameId
                    ? _data.Settings with
                    {
                        SelectedCompactGameId = normalized.IsEmpty
                            ? null
                            : normalized[0].Id,
                    }
                    : _data.Settings;
            DataEnvelope candidate = _data with
            {
                Games = normalized,
                Settings = settings,
            };
            await SaveAndPublishAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
            deleted = true;
        }
        finally
        {
            _mutationGate.Release();
        }

        if (deleted)
        {
            await NotifyGamesChangedAsync().ConfigureAwait(false);
        }

        return deleted;
    }

    public async Task<AppSettings> UpdateSettingsAsync(
        Func<AppSettings, AppSettings> update,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(update);
        AppSettings updated;
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            updated = update(_data.Settings)
                ?? throw new InvalidOperationException(
                    "設定の更新結果がありません。");
            if (updated == _data.Settings)
            {
                return updated;
            }

            DataEnvelope candidate = _data with { Settings = updated };
            await SaveAndPublishAsync(candidate, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _mutationGate.Release();
        }

        return updated;
    }

    public async Task<RecoveryPromotionResult> PromoteRecoveryAsync(
        CancellationToken cancellationToken)
    {
        RecoveryPromotionResult result;
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            result = await _dataStore.PromoteRecoveryAsync(
                cancellationToken).ConfigureAwait(false);
            _data = result.Envelope with
            {
                Games = NormalizeOrder(result.Envelope.Games),
            };
        }
        finally
        {
            _mutationGate.Release();
        }

        await NotifyGamesChangedAsync().ConfigureAwait(false);
        return result;
    }

    public async Task ReplaceFromRestoreAsync(
        DataEnvelope restoredData,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(restoredData);
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            _data = restoredData with
            {
                Games = NormalizeOrder(restoredData.Games),
            };
        }
        finally
        {
            _mutationGate.Release();
        }

        await NotifyGamesChangedAsync().ConfigureAwait(false);
    }

    internal async Task<BackupRestoreResult> CommitRestoreAsync(
        Func<
            Func<DataEnvelope, CancellationToken, Task>,
            CancellationToken,
            Task<BackupRestoreResult>> commitAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(commitAsync);
        BackupRestoreResult result;
        bool wasPublished = false;
        await _mutationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            EnsureInitialized();
            result = await commitAsync(
                    (data, _) =>
                    {
                        _data = data with
                        {
                            Games = NormalizeOrder(data.Games),
                        };
                        wasPublished = true;
                        return Task.CompletedTask;
                    },
                    cancellationToken)
                .ConfigureAwait(false);
            if (!wasPublished)
            {
                throw new InvalidOperationException(
                    "復元データがメモリへ反映されませんでした。");
            }
        }
        finally
        {
            _mutationGate.Release();
        }

        await NotifyGamesChangedAsync().ConfigureAwait(false);
        return result;
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

    private void EnsureInitialized()
    {
        if (!_isInitialized)
        {
            throw new InvalidOperationException(
                "ゲーム管理の初期化が完了していません。");
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
    }

    private async Task NotifyGamesChangedAsync()
    {
        GamesChangedHandler? subscribers = GamesChanged;
        if (subscribers is null)
        {
            LastNotificationError = null;
            return;
        }

        List<Exception>? errors = null;
        foreach (GamesChangedHandler subscriber in
            subscribers.GetInvocationList())
        {
            try
            {
                await subscriber(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                errors ??= [];
                errors.Add(exception);
            }
        }

        LastNotificationError = errors?.Count switch
        {
            null => null,
            1 => errors[0],
            _ => new AggregateException(errors),
        };
    }
}
