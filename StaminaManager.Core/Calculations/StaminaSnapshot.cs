using StaminaManager.Core.Models;

namespace StaminaManager.Core.Calculations;

public sealed record StaminaSnapshot(
    int Current,
    int Maximum,
    double Ratio,
    StaminaStatus Status,
    DateTimeOffset? FullAtUtc,
    TimeSpan Remaining);
