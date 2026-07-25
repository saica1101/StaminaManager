namespace StaminaManager.Core.Calculations;

public static class OverviewLayoutPolicy
{
    public const double ThreeColumnMinimumWidth = 840d;
    public const double TwoColumnMinimumWidth = 560d;

    public static int GetColumns(double contentWidth)
    {
        if (!double.IsFinite(contentWidth) || contentWidth < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(contentWidth));
        }

        if (contentWidth >= ThreeColumnMinimumWidth)
        {
            return 3;
        }

        return contentWidth >= TwoColumnMinimumWidth ? 2 : 1;
    }
}
