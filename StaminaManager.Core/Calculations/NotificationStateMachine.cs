using StaminaManager.Core.Models;

namespace StaminaManager.Core.Calculations;

public static class NotificationStateMachine
{
    public static NotificationDecision Evaluate(
        GameEntry game,
        int leadMinutes,
        DateTimeOffset nowUtc,
        bool notificationsEnabled,
        NotificationLedgerEntry? existingEntry,
        bool isScheduledInWindows)
    {
        ArgumentNullException.ThrowIfNull(game);
        nowUtc = nowUtc.ToUniversalTime();
        StaminaSnapshot snapshot = StaminaCalculator.Calculate(game, nowUtc);
        if (snapshot.FullAtUtc is null)
        {
            return new NotificationDecision(
                ShouldCancel(existingEntry, isScheduledInWindows)
                    ? NotificationPlatformAction.Cancel
                    : NotificationPlatformAction.None,
                Entry: null);
        }

        if (!NotificationCycle.TryCreate(
            game,
            snapshot.FullAtUtc.Value,
            leadMinutes,
            NotificationState.Scheduled,
            out NotificationLedgerEntry? candidate))
        {
            return new NotificationDecision(
                NotificationPlatformAction.None,
                existingEntry,
                NotificationDecisionError.InvalidSchedule,
                IsExistingSchedulePreserved: existingEntry is not null);
        }

        bool isSameCycle = existingEntry?.Key == candidate.Key;
        if (!notificationsEnabled)
        {
            return new NotificationDecision(
                ShouldCancel(existingEntry, isScheduledInWindows)
                    ? NotificationPlatformAction.Cancel
                    : NotificationPlatformAction.None,
                candidate with { State = NotificationState.Suppressed });
        }

        if (isSameCycle
            && existingEntry!.State == NotificationState.Consumed)
        {
            return new NotificationDecision(
                NotificationPlatformAction.None,
                existingEntry);
        }

        if (candidate.NotificationAtUtcTicks > nowUtc.UtcTicks)
        {
            return EvaluateFuture(
                candidate,
                existingEntry,
                isSameCycle,
                isScheduledInWindows);
        }

        if (snapshot.FullAtUtc.Value > nowUtc)
        {
            return EvaluatePastDue(
                candidate,
                existingEntry,
                isSameCycle,
                isScheduledInWindows);
        }

        NotificationLedgerEntry consumed = candidate with
        {
            State = NotificationState.Consumed,
        };
        return new NotificationDecision(
            ShouldCancel(existingEntry, isScheduledInWindows)
                ? NotificationPlatformAction.Cancel
                : NotificationPlatformAction.None,
            isSameCycle ? consumed : null);
    }

    private static NotificationDecision EvaluateFuture(
        NotificationLedgerEntry candidate,
        NotificationLedgerEntry? existingEntry,
        bool isSameCycle,
        bool isScheduledInWindows)
    {
        if (!isSameCycle)
        {
            return new NotificationDecision(
                existingEntry is null && !isScheduledInWindows
                    ? NotificationPlatformAction.Schedule
                    : NotificationPlatformAction.ReplaceScheduled,
                candidate);
        }

        if (existingEntry!.State == NotificationState.Suppressed)
        {
            return new NotificationDecision(
                NotificationPlatformAction.Schedule,
                candidate);
        }

        bool isContentCurrent = string.Equals(
            existingEntry.ContentFingerprint,
            candidate.ContentFingerprint,
            StringComparison.Ordinal);
        if (isScheduledInWindows && isContentCurrent)
        {
            return new NotificationDecision(
                NotificationPlatformAction.None,
                existingEntry);
        }

        return new NotificationDecision(
            NotificationPlatformAction.ReplaceScheduled,
            candidate);
    }

    private static NotificationDecision EvaluatePastDue(
        NotificationLedgerEntry candidate,
        NotificationLedgerEntry? existingEntry,
        bool isSameCycle,
        bool isScheduledInWindows)
    {
        if (!isSameCycle)
        {
            return new NotificationDecision(
                NotificationPlatformAction.ShowImmediate,
                candidate with { State = NotificationState.Consumed });
        }

        if (existingEntry!.State == NotificationState.Suppressed)
        {
            return new NotificationDecision(
                NotificationPlatformAction.None,
                candidate with { State = NotificationState.Consumed });
        }

        if (isScheduledInWindows)
        {
            return new NotificationDecision(
                NotificationPlatformAction.None,
                existingEntry);
        }

        return new NotificationDecision(
            NotificationPlatformAction.None,
            candidate with { State = NotificationState.Consumed });
    }

    private static bool ShouldCancel(
        NotificationLedgerEntry? existingEntry,
        bool isScheduledInWindows) => isScheduledInWindows
            || existingEntry?.State == NotificationState.Scheduled;
}
