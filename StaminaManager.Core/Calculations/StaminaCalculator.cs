using StaminaManager.Core.Models;

namespace StaminaManager.Core.Calculations;

public static class StaminaCalculator
{
    private const string FullTimeOutOfRangeMessage =
        "満タン時刻が DateTimeOffset の表現範囲外です。";
    private const long AttentionPercent = 50;
    private const long NearFullPercent = 80;
    private const long FullPercent = 100;

    public static StaminaSnapshot Calculate(
        GameEntry entry,
        DateTimeOffset nowUtc)
    {
        DateTimeOffset recordedAtUtc =
            entry.RecordedAtUtc.ToUniversalTime();
        nowUtc = nowUtc.ToUniversalTime();

        if (entry.BaseStamina >= entry.MaxStamina)
        {
            StaminaStatus status = entry.BaseStamina == entry.MaxStamina
                ? StaminaStatus.Full
                : StaminaStatus.OverCap;

            return new StaminaSnapshot(
                entry.BaseStamina,
                entry.MaxStamina,
                1.0,
                status,
                null,
                TimeSpan.Zero);
        }

        long elapsedMinutes = Math.Max(
            0L,
            (long)Math.Floor(
                (nowUtc - recordedAtUtc).TotalMinutes));
        long recovered = elapsedMinutes / entry.RecoveryMinutes;
        int current = (int)Math.Min(
            entry.MaxStamina,
            (long)entry.BaseStamina + recovered);
        DateTimeOffset fullAtUtc =
            CalculateFullAtUtc(entry, recordedAtUtc);
        TimeSpan remaining = fullAtUtc > nowUtc
            ? fullAtUtc - nowUtc
            : TimeSpan.Zero;

        return new StaminaSnapshot(
            current,
            entry.MaxStamina,
            Math.Min((double)current / entry.MaxStamina, 1.0),
            GetStatus(current, entry.MaxStamina),
            fullAtUtc,
            remaining);
    }

    private static DateTimeOffset CalculateFullAtUtc(
        GameEntry entry,
        DateTimeOffset recordedAtUtc)
    {
        long recoveryTicks;

        try
        {
            long remainingStamina =
                (long)entry.MaxStamina - entry.BaseStamina;
            long recoveryMinutes = checked(
                remainingStamina * entry.RecoveryMinutes);
            recoveryTicks = checked(
                recoveryMinutes * TimeSpan.TicksPerMinute);
        }
        catch (OverflowException)
        {
            throw CreateFullTimeOutOfRangeException();
        }

        try
        {
            return recordedAtUtc.AddTicks(recoveryTicks);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw CreateFullTimeOutOfRangeException();
        }

        ArgumentOutOfRangeException
            CreateFullTimeOutOfRangeException() => new(
                nameof(entry),
                FullTimeOutOfRangeMessage);
    }

    private static StaminaStatus GetStatus(int current, int maximum)
    {
        long scaledCurrent = (long)current * FullPercent;

        if (scaledCurrent >= (long)maximum * FullPercent)
        {
            return StaminaStatus.Full;
        }

        if (scaledCurrent > (long)maximum * NearFullPercent)
        {
            return StaminaStatus.NearFull;
        }

        if (scaledCurrent >= (long)maximum * AttentionPercent)
        {
            return StaminaStatus.Attention;
        }

        return StaminaStatus.Safe;
    }
}
