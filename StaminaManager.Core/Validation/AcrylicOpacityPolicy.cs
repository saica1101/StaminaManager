namespace StaminaManager.Core.Validation;

public static class AcrylicOpacityPolicy
{
    public const int MinAcrylicTintOpacityPercent = 0;

    public const int MaxAcrylicTintOpacityPercent = 100;

    public const int DefaultAcrylicTintOpacityPercent = 80;

    public static bool IsValid(int percent) => percent is >=
        MinAcrylicTintOpacityPercent and <= MaxAcrylicTintOpacityPercent;
}
