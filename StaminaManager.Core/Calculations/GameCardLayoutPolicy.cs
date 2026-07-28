namespace StaminaManager.Core.Calculations;

public static class GameCardLayoutPolicy
{
    public const double RegularLayoutMinimumWidth = 272d;

    public static bool ShouldUseNarrowLayout(double cardWidth)
    {
        if (!double.IsFinite(cardWidth) || cardWidth < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(cardWidth));
        }

        return cardWidth < RegularLayoutMinimumWidth;
    }
}
