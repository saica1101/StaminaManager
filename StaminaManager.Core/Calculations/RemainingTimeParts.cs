namespace StaminaManager.Core.Calculations;

public sealed record RemainingTimeParts(
    bool IsFull,
    int Days,
    int Hours,
    int Minutes)
{
    private const long MinutesPerHour = 60;
    private const long MinutesPerDay = 24 * MinutesPerHour;

    public static RemainingTimeParts From(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return new RemainingTimeParts(
                IsFull: true,
                Days: 0,
                Hours: 0,
                Minutes: 0);
        }

        long totalMinutes = checked(
            (long)Math.Ceiling(remaining.TotalMinutes));
        int days = checked((int)(totalMinutes / MinutesPerDay));
        int hours = checked((int)(
            totalMinutes % MinutesPerDay / MinutesPerHour));
        int minutes = checked((int)(totalMinutes % MinutesPerHour));
        return new RemainingTimeParts(
            IsFull: false,
            days,
            hours,
            minutes);
    }
}
