using StaminaManager.Core.Validation;

namespace StaminaManager.Core.Models;

public sealed record BackdropRequest
{
    public BackdropRequest(
        BackdropKind kind,
        int acrylicTintOpacityPercent)
    {
        if (!AcrylicOpacityPolicy.IsValid(acrylicTintOpacityPercent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(acrylicTintOpacityPercent));
        }

        Kind = kind;
        AcrylicTintOpacityPercent = acrylicTintOpacityPercent;
    }

    public BackdropKind Kind { get; }

    public int AcrylicTintOpacityPercent { get; }
}
