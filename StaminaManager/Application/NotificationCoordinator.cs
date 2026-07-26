using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;
using System.Collections.Immutable;

namespace StaminaManager.Application;

public interface INotificationReconciler
{
    Task<NotificationReconcileResult> ReconcileAsync(
        IReadOnlyCollection<GameEntry> games,
        AppSettings settings,
        CancellationToken cancellationToken);
}

public sealed record NotificationReconcileIssue(
    Guid? GameId,
    NotificationDecisionError Error,
    string FailureType);

public sealed record NotificationReconcileResult(
    ImmutableArray<NotificationReconcileIssue> Issues)
{
    public bool HasFailures => !Issues.IsEmpty;

    public bool HasInvalidSchedule => Issues.Any(
        issue => issue.Error == NotificationDecisionError.InvalidSchedule);

    public static NotificationReconcileResult Success { get; } = new(
        ImmutableArray<NotificationReconcileIssue>.Empty);
}

public sealed class NotificationCoordinator : INotificationReconciler
{
    private readonly INotificationScheduler _scheduler;
    private readonly INotificationLedgerStore _ledgerStore;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _reconcileGate = new(1, 1);

    public NotificationCoordinator(
        INotificationScheduler scheduler,
        INotificationLedgerStore ledgerStore,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(ledgerStore);
        ArgumentNullException.ThrowIfNull(clock);
        _scheduler = scheduler;
        _ledgerStore = ledgerStore;
        _clock = clock;
    }

    public async Task<NotificationReconcileResult> ReconcileAsync(
        IReadOnlyCollection<GameEntry> games,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(games);
        ArgumentNullException.ThrowIfNull(settings);
        await _reconcileGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await ReconcileCoreAsync(
                games,
                settings,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _reconcileGate.Release();
        }
    }

    private async Task<NotificationReconcileResult> ReconcileCoreAsync(
        IReadOnlyCollection<GameEntry> games,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<NotificationLedgerEntry> loaded =
            await _ledgerStore.LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        List<NotificationLedgerEntry> entries = [.. loaded];
        HashSet<Guid> currentGameIds = games
            .Select(game => game.Id)
            .ToHashSet();
        bool isLedgerDirty = entries.RemoveAll(
            entry => !currentGameIds.Contains(entry.GameId)) > 0;
        HashSet<string> retainedCycleKeys = ResolveRetainedCycleKeys(
            games,
            settings.NotificationLeadMinutes);
        isLedgerDirty |= PruneLedger(entries, retainedCycleKeys);
        bool hasLedgerSaveFailure = false;

        IReadOnlySet<Guid> scheduledGameIds =
            await _scheduler.GetScheduledGameIdsAsync(cancellationToken)
                .ConfigureAwait(false);
        HashSet<Guid> scheduled = scheduledGameIds.ToHashSet();
        if (!settings.NotificationsEnabled)
        {
            return await SuppressAllAsync(
                games,
                settings,
                entries,
                scheduled,
                cancellationToken).ConfigureAwait(false);
        }

        ImmutableArray<NotificationReconcileIssue>.Builder issues =
            ImmutableArray.CreateBuilder<NotificationReconcileIssue>();
        foreach (Guid orphanedGameId in scheduled
            .Where(gameId => !currentGameIds.Contains(gameId))
            .ToArray())
        {
            try
            {
                await _scheduler.CancelAsync(
                    orphanedGameId,
                    cancellationToken).ConfigureAwait(false);
                scheduled.Remove(orphanedGameId);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                issues.Add(new NotificationReconcileIssue(
                    orphanedGameId,
                    NotificationDecisionError.None,
                    exception.GetType().Name));
            }
        }

        try
        {
            foreach (GameEntry game in games)
            {
                cancellationToken.ThrowIfCancellationRequested();
                NotificationLedgerEntry? existing = ResolveExisting(
                    entries,
                    game,
                    settings.NotificationLeadMinutes);
                NotificationDecision decision =
                    NotificationStateMachine.Evaluate(
                        game,
                        settings.NotificationLeadMinutes,
                        _clock.UtcNow,
                        notificationsEnabled: true,
                        existing,
                        scheduled.Contains(game.Id));
                if (decision.Error != NotificationDecisionError.None)
                {
                    issues.Add(new NotificationReconcileIssue(
                        game.Id,
                        decision.Error,
                        FailureType: string.Empty));
                    continue;
                }

                try
                {
                    await ApplyPlatformActionAsync(
                        game,
                        decision,
                        scheduled,
                        cancellationToken).ConfigureAwait(false);
                    isLedgerDirty |= UpdateLedger(
                        entries,
                        game.Id,
                        decision);
                    if (decision.Entry is not null)
                    {
                        retainedCycleKeys.Add(decision.Entry.Key);
                    }

                    if (isLedgerDirty
                        && !hasLedgerSaveFailure
                        && RequiresImmediateCheckpoint(decision, existing))
                    {
                        isLedgerDirty |= PruneLedger(
                            entries,
                            retainedCycleKeys);
                        bool isSaved = await SaveLedgerAsync(
                            entries,
                            issues,
                            CancellationToken.None).ConfigureAwait(false);
                        isLedgerDirty = !isSaved;
                        hasLedgerSaveFailure = !isSaved;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (!IsProcessFatal(exception))
                {
                    issues.Add(new NotificationReconcileIssue(
                        game.Id,
                        NotificationDecisionError.None,
                        exception.GetType().Name));
                }
            }
        }
        catch (OperationCanceledException)
        {
            if (isLedgerDirty && !hasLedgerSaveFailure)
            {
                isLedgerDirty |= PruneLedger(entries, retainedCycleKeys);
                await SaveLedgerAsync(
                    entries,
                    issues,
                    CancellationToken.None).ConfigureAwait(false);
            }

            throw;
        }

        if (isLedgerDirty && !hasLedgerSaveFailure)
        {
            isLedgerDirty |= PruneLedger(entries, retainedCycleKeys);
            await SaveLedgerAsync(
                entries,
                issues,
                CancellationToken.None).ConfigureAwait(false);
        }

        return new NotificationReconcileResult(issues.ToImmutable());
    }

    private async Task<NotificationReconcileResult> SuppressAllAsync(
        IReadOnlyCollection<GameEntry> games,
        AppSettings settings,
        List<NotificationLedgerEntry> entries,
        IReadOnlySet<Guid> scheduled,
        CancellationToken cancellationToken)
    {
        ImmutableArray<NotificationReconcileIssue>.Builder issues =
            ImmutableArray.CreateBuilder<NotificationReconcileIssue>();
        try
        {
            await _scheduler.CancelAllAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            issues.Add(new NotificationReconcileIssue(
                GameId: null,
                NotificationDecisionError.None,
                exception.GetType().Name));
            return new NotificationReconcileResult(issues.ToImmutable());
        }

        foreach (GameEntry game in games)
        {
            NotificationLedgerEntry? existing = ResolveExisting(
                entries,
                game,
                settings.NotificationLeadMinutes);
            NotificationDecision decision = NotificationStateMachine.Evaluate(
                game,
                settings.NotificationLeadMinutes,
                _clock.UtcNow,
                notificationsEnabled: false,
                existing,
                scheduled.Contains(game.Id));
            if (decision.Error != NotificationDecisionError.None)
            {
                issues.Add(new NotificationReconcileIssue(
                    game.Id,
                    decision.Error,
                    FailureType: string.Empty));
                if (existing?.State == NotificationState.Scheduled)
                {
                    _ = UpdateLedger(
                        entries,
                        game.Id,
                        decision with
                        {
                            Entry = existing with
                            {
                                State = NotificationState.Suppressed,
                            },
                        });
                }

                continue;
            }

            _ = UpdateLedger(entries, game.Id, decision);
        }

        PruneLedger(
            entries,
            ResolveRetainedCycleKeys(
                games,
                settings.NotificationLeadMinutes));
        await SaveLedgerAsync(entries, issues, CancellationToken.None)
            .ConfigureAwait(false);
        return new NotificationReconcileResult(issues.ToImmutable());
    }

    private async Task ApplyPlatformActionAsync(
        GameEntry game,
        NotificationDecision decision,
        HashSet<Guid> scheduled,
        CancellationToken cancellationToken)
    {
        switch (decision.Action)
        {
            case NotificationPlatformAction.None:
                return;
            case NotificationPlatformAction.Cancel:
                await _scheduler.CancelAsync(game.Id, cancellationToken)
                    .ConfigureAwait(false);
                scheduled.Remove(game.Id);
                return;
            case NotificationPlatformAction.ReplaceScheduled:
                await _scheduler.CancelAsync(game.Id, cancellationToken)
                    .ConfigureAwait(false);
                scheduled.Remove(game.Id);
                goto case NotificationPlatformAction.Schedule;
            case NotificationPlatformAction.Schedule:
                await _scheduler.ScheduleAsync(
                    CreateRequest(game, decision.Entry!),
                    cancellationToken).ConfigureAwait(false);
                scheduled.Add(game.Id);
                return;
            case NotificationPlatformAction.ShowImmediate:
                if (scheduled.Contains(game.Id))
                {
                    await _scheduler.CancelAsync(
                        game.Id,
                        cancellationToken).ConfigureAwait(false);
                    scheduled.Remove(game.Id);
                }

                await _scheduler.ShowImmediateAsync(
                    CreateRequest(game, decision.Entry!),
                    cancellationToken).ConfigureAwait(false);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(decision));
        }
    }

    private static NotificationRequest CreateRequest(
        GameEntry game,
        NotificationLedgerEntry entry) => new(
            game.Id,
            game.Name,
            new DateTimeOffset(entry.FullAtUtcTicks, TimeSpan.Zero),
            new DateTimeOffset(
                entry.NotificationAtUtcTicks,
                TimeSpan.Zero));

    private static NotificationLedgerEntry? ResolveExisting(
        IReadOnlyCollection<NotificationLedgerEntry> entries,
        GameEntry game,
        int leadMinutes)
    {
        try
        {
            StaminaSnapshot snapshot = StaminaCalculator.Calculate(
                game,
                game.RecordedAtUtc);
            if (snapshot.FullAtUtc is not null)
            {
                string currentKey = NotificationLedgerEntry.Create(
                    game,
                    snapshot.FullAtUtc.Value,
                    leadMinutes,
                    NotificationState.Scheduled).Key;
                NotificationLedgerEntry? current = entries.FirstOrDefault(
                    entry => string.Equals(
                        entry.Key,
                        currentKey,
                        StringComparison.Ordinal));
                if (current is not null)
                {
                    return current;
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            // EvaluateがInvalidScheduleとして扱い、既存予約を保持する。
        }

        return entries
            .Where(entry => entry.GameId == game.Id)
            .OrderByDescending(entry =>
                entry.State == NotificationState.Scheduled)
            .ThenByDescending(entry => entry.FullAtUtcTicks)
            .FirstOrDefault();
    }

    private static bool UpdateLedger(
        List<NotificationLedgerEntry> entries,
        Guid gameId,
        NotificationDecision decision)
    {
        NotificationLedgerEntry[] previous = entries
            .Where(entry => entry.GameId == gameId)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
        if (decision.Entry is null)
        {
            entries.RemoveAll(entry => entry.GameId == gameId
                && entry.State == NotificationState.Scheduled);
        }
        else
        {
            entries.RemoveAll(entry => string.Equals(
                entry.Key,
                decision.Entry.Key,
                StringComparison.Ordinal));
            entries.RemoveAll(entry => entry.GameId == gameId
                && entry.State == NotificationState.Scheduled);

            entries.Add(decision.Entry);
        }

        NotificationLedgerEntry[] current = entries
            .Where(entry => entry.GameId == gameId)
            .OrderBy(entry => entry.Key, StringComparer.Ordinal)
            .ToArray();
        return !previous.SequenceEqual(current);
    }

    private static bool RequiresImmediateCheckpoint(
        NotificationDecision decision,
        NotificationLedgerEntry? existingEntry) =>
        decision.Action == NotificationPlatformAction.ShowImmediate
        || (decision.Entry?.State == NotificationState.Consumed
            && existingEntry?.State == NotificationState.Scheduled
            && string.Equals(
                decision.Entry.Key,
                existingEntry.Key,
                StringComparison.Ordinal));

    private static HashSet<string> ResolveRetainedCycleKeys(
        IReadOnlyCollection<GameEntry> games,
        int leadMinutes)
    {
        HashSet<string> keys = new(StringComparer.Ordinal);
        foreach (GameEntry game in games)
        {
            try
            {
                StaminaSnapshot snapshot = StaminaCalculator.Calculate(
                    game,
                    game.RecordedAtUtc);
                if (snapshot.FullAtUtc is not null)
                {
                    keys.Add(NotificationLedgerEntry.Create(
                        game,
                        snapshot.FullAtUtc.Value,
                        leadMinutes,
                        NotificationState.Scheduled).Key);
                }
            }
            catch (ArgumentOutOfRangeException)
            {
                // InvalidScheduleとして後段で報告し、既存台帳は保護しない。
            }
        }

        return keys;
    }

    private static bool PruneLedger(
        List<NotificationLedgerEntry> entries,
        IReadOnlySet<string> retainedCycleKeys)
    {
        if (entries.Count
            <= NotificationLedgerLimits.MaxRetainedEntryCount)
        {
            return false;
        }

        HashSet<string> retainedKeys = entries
            .OrderByDescending(entry => retainedCycleKeys.Contains(entry.Key))
            .ThenByDescending(entry => entry.FullAtUtcTicks)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(NotificationLedgerLimits.MaxRetainedEntryCount)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        return entries.RemoveAll(
            entry => !retainedKeys.Contains(entry.Key)) > 0;
    }

    private async Task<bool> SaveLedgerAsync(
        IReadOnlyCollection<NotificationLedgerEntry> entries,
        ImmutableArray<NotificationReconcileIssue>.Builder issues,
        CancellationToken cancellationToken)
    {
        try
        {
            await _ledgerStore.SaveAsync(entries, cancellationToken)
                .ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            issues.Add(new NotificationReconcileIssue(
                GameId: null,
                NotificationDecisionError.None,
                exception.GetType().Name));
            return false;
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
}
