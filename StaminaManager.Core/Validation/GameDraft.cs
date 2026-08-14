namespace StaminaManager.Core.Validation;

public sealed record GameDraft(
    string Name,
    int CurrentStamina,
    int MaxStamina,
    int RecoveryMinutes,
    string? ImageAssetId,
    int RecoverySeconds,
    bool IsNotificationEnabled,
    int? NotificationLeadMinutesOverride = null);
