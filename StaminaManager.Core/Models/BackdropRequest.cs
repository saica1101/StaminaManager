namespace StaminaManager.Core.Models;

public sealed record BackdropRequest(
    BackdropKind Kind,
    int AcrylicTintOpacityPercent);
