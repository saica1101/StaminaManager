namespace StaminaManager.Core.Calculations;

public sealed record RemainingTimeParts(
    bool IsFull,
    int Days,
    int Hours,
    int Minutes,
    int Seconds)
{
    private const long SecondsPerMinute = 60;
    private const long SecondsPerHour = 60 * SecondsPerMinute;
    private const long MinutesPerHour = 60;
    private const long MinutesPerDay = 24 * MinutesPerHour;
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1);

    public static RemainingTimeParts From(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
        {
            return new RemainingTimeParts(
                IsFull: true,
                Days: 0,
                Hours: 0,
                Minutes: 0,
                Seconds: 0);
        }

        if (remaining < OneDay)
        {
            long totalSeconds = checked(
                (long)Math.Ceiling(remaining.TotalSeconds));
            int subdayHours = checked((int)(
                totalSeconds / SecondsPerHour));
            int subdayMinutes = checked((int)(
                totalSeconds % SecondsPerHour / SecondsPerMinute));
            int subdaySeconds = checked((int)(
                totalSeconds % SecondsPerMinute));
            return new RemainingTimeParts(
                IsFull: false,
                Days: 0,
                Hours: subdayHours,
                Minutes: subdayMinutes,
                Seconds: subdaySeconds);
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
            minutes,
            Seconds: 0);
    }
}
