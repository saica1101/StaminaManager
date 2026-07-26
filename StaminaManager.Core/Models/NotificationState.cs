namespace StaminaManager.Core.Models;

public enum NotificationState
{
    Scheduled,
    Consumed,
    Suppressed,
}

public enum NotificationPlatformAction
{
    None,
    Schedule,
    ReplaceScheduled,
    ShowImmediate,
    Cancel,
}

public enum NotificationDecisionError
{
    None,
    InvalidSchedule,
}

public sealed record NotificationDecision(
    NotificationPlatformAction Action,
    NotificationLedgerEntry? Entry,
    NotificationDecisionError Error = NotificationDecisionError.None,
    bool IsExistingSchedulePreserved = false);
