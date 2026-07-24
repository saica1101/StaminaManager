namespace StaminaManager.Core.Models;

public sealed record GameEntry(
    Guid Id,
    string Name,
    int BaseStamina,
    int MaxStamina,
    int RecoveryMinutes,
    DateTimeOffset RecordedAtUtc,
    string? ImageAssetId,
    int SortOrder);
