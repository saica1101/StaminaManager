namespace StaminaManager.Core.Calculations;

public static class StaminaRingMath
{
    public const double FullSweepDegrees = 359.99d;

    public static double GetSweepDegrees(double ratio)
    {
        if (!double.IsFinite(ratio))
        {
            throw new ArgumentOutOfRangeException(nameof(ratio));
        }

        double normalizedRatio = Math.Clamp(ratio, 0d, 1d);
        return normalizedRatio >= 1d
            ? FullSweepDegrees
            : normalizedRatio * 360d;
    }
}
