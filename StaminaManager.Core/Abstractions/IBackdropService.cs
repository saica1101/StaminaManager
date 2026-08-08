using StaminaManager.Core.Models;

namespace StaminaManager.Core.Abstractions;

public enum BackdropFallbackReason
{
    None,
    HighContrast,
    TransparencyDisabled,
    RemoteSession,
    Unsupported,
    ApplyFailed,
    SolidFallbackFailed,
}

public sealed record BackdropResult(
    BackdropKind RequestedBackdrop,
    BackdropKind ActualBackdrop,
    BackdropFallbackReason FallbackReason,
    string? ErrorMessage,
    int? ActualAcrylicTintOpacityPercent = null)
{
    public bool IsRequestedBackdropApplied =>
        RequestedBackdrop == ActualBackdrop
        && FallbackReason == BackdropFallbackReason.None;
}

public interface IBackdropService
{
    BackdropResult Apply(BackdropRequest request);
}
