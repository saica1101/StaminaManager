namespace StaminaManager.Core.Validation;

public enum ValidationErrorCode
{
    NameRequired,
    CurrentStaminaOutOfRange,
    MaxStaminaOutOfRange,
    RecoveryMinutesOutOfRange,
    RecoverySecondsOutOfRange,
    RecoveryIntervalOutOfRange,
    FullTimeOutOfRange,
    NotificationLeadMinutesOverrideOutOfRange,
}
