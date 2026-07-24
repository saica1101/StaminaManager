using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;

namespace StaminaManager.Tests.Calculations;

[TestClass]
public sealed class StaminaCalculatorTests
{
    private const string FullTimeOutOfRangeMessage =
        "満タン時刻が DateTimeOffset の表現範囲外です。";

    private static readonly DateTimeOffset RecordedAtUtc = new(
        2026,
        7,
        25,
        0,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    [DataRow(49, 100, StaminaStatus.Safe)]
    [DataRow(50, 100, StaminaStatus.Attention)]
    [DataRow(80, 100, StaminaStatus.Attention)]
    [DataRow(81, 100, StaminaStatus.NearFull)]
    [DataRow(100, 100, StaminaStatus.Full)]
    [DataRow(101, 100, StaminaStatus.OverCap)]
    public void Calculate_UsesExactThresholds(
        int current,
        int maximum,
        StaminaStatus expected)
    {
        GameEntry entry = CreateEntry(current, maximum, recoveryMinutes: 5);

        StaminaSnapshot snapshot =
            StaminaCalculator.Calculate(entry, RecordedAtUtc);

        Assert.AreEqual(expected, snapshot.Status);
    }

    [TestMethod]
    [DataRow(4, 5, StaminaStatus.Attention)]
    [DataRow(5, 6, StaminaStatus.NearFull)]
    [DataRow(1, 3, StaminaStatus.Safe)]
    [DataRow(2, 3, StaminaStatus.Attention)]
    public void Calculate_UsesExactThresholdsForNonHundredMaximum(
        int current,
        int maximum,
        StaminaStatus expected)
    {
        GameEntry entry = CreateEntry(current, maximum, recoveryMinutes: 5);

        StaminaSnapshot snapshot =
            StaminaCalculator.Calculate(entry, RecordedAtUtc);

        Assert.AreEqual(expected, snapshot.Status);
    }

    [TestMethod]
    public void Calculate_RecoversStaminaForElapsedWholeIntervals()
    {
        GameEntry entry = CreateEntry(
            baseStamina: 40,
            maxStamina: 100,
            recoveryMinutes: 5);

        StaminaSnapshot snapshot = StaminaCalculator.Calculate(
            entry,
            RecordedAtUtc.AddMinutes(15));

        Assert.AreEqual(43, snapshot.Current);
        Assert.AreEqual(100, snapshot.Maximum);
        Assert.AreEqual(0.43, snapshot.Ratio, 0.000_000_001);
        Assert.AreEqual(StaminaStatus.Safe, snapshot.Status);
        Assert.AreEqual(
            RecordedAtUtc.AddMinutes(300),
            snapshot.FullAtUtc);
        Assert.AreEqual(TimeSpan.FromMinutes(285), snapshot.Remaining);
    }

    [TestMethod]
    public void Calculate_TruncatesElapsedTimeBelowOneRecoveryInterval()
    {
        GameEntry entry = CreateEntry(
            baseStamina: 40,
            maxStamina: 100,
            recoveryMinutes: 5);

        StaminaSnapshot snapshot = StaminaCalculator.Calculate(
            entry,
            RecordedAtUtc.AddMinutes(4).AddSeconds(59));

        Assert.AreEqual(40, snapshot.Current);
    }

    [TestMethod]
    public void Calculate_ClampsElapsedTimeToZeroWhenNowIsBeforeRecordedAt()
    {
        GameEntry entry = CreateEntry(
            baseStamina: 40,
            maxStamina: 100,
            recoveryMinutes: 5);

        StaminaSnapshot snapshot = StaminaCalculator.Calculate(
            entry,
            RecordedAtUtc.AddMinutes(-30));

        Assert.AreEqual(40, snapshot.Current);
    }

    [TestMethod]
    public void Calculate_PreservesExactFullTimeAfterStaminaIsFull()
    {
        GameEntry entry = CreateEntry(
            baseStamina: 97,
            maxStamina: 100,
            recoveryMinutes: 7);
        DateTimeOffset expectedFullAt = RecordedAtUtc.AddMinutes(21);

        StaminaSnapshot snapshot = StaminaCalculator.Calculate(
            entry,
            expectedFullAt.AddHours(1));

        Assert.AreEqual(100, snapshot.Current);
        Assert.AreEqual(expectedFullAt, snapshot.FullAtUtc);
        Assert.AreEqual(TimeSpan.Zero, snapshot.Remaining);
    }

    [TestMethod]
    public void Calculate_PreservesBaseStaminaWhenItExceedsMaximum()
    {
        GameEntry entry = CreateEntry(
            baseStamina: 120,
            maxStamina: 100,
            recoveryMinutes: 5);

        StaminaSnapshot snapshot = StaminaCalculator.Calculate(
            entry,
            RecordedAtUtc.AddDays(1));

        Assert.AreEqual(120, snapshot.Current);
        Assert.AreEqual(StaminaStatus.OverCap, snapshot.Status);
        Assert.IsNull(snapshot.FullAtUtc);
        Assert.AreEqual(TimeSpan.Zero, snapshot.Remaining);
    }

    [TestMethod]
    public void Calculate_CapsRingRatioAtOne()
    {
        GameEntry entry = CreateEntry(
            baseStamina: 101,
            maxStamina: 100,
            recoveryMinutes: 5);

        StaminaSnapshot snapshot =
            StaminaCalculator.Calculate(entry, RecordedAtUtc);

        Assert.AreEqual(1.0, snapshot.Ratio);
    }

    [TestMethod]
    public void Calculate_RejectsFullTimeOutsideDateTimeOffsetRange()
    {
        GameEntry entry = new(
            Id: Guid.NewGuid(),
            Name: "Overflow test",
            BaseStamina: 0,
            MaxStamina: 2,
            RecoveryMinutes: 1,
            RecordedAtUtc: DateTimeOffset.MaxValue.AddMinutes(-1),
            ImageAssetId: null,
            SortOrder: 0);

        AssertFullTimeOutOfRangeException(
            () => StaminaCalculator.Calculate(
                entry,
                entry.RecordedAtUtc));
    }

    [TestMethod]
    public void Calculate_UsesSameContractWhenFullTimeMultiplicationOverflows()
    {
        GameEntry entry = new(
            Id: Guid.NewGuid(),
            Name: "Multiplication overflow test",
            BaseStamina: int.MinValue,
            MaxStamina: int.MaxValue,
            RecoveryMinutes: int.MaxValue,
            RecordedAtUtc: RecordedAtUtc,
            ImageAssetId: null,
            SortOrder: 0);

        AssertFullTimeOutOfRangeException(
            () => StaminaCalculator.Calculate(entry, RecordedAtUtc));
    }

    [TestMethod]
    public void Calculate_ReturnsFullTimeInUtcForRecordedTimeWithOffset()
    {
        DateTimeOffset recordedAt = new(
            2026,
            7,
            25,
            0,
            0,
            0,
            TimeSpan.FromHours(9));
        GameEntry entry = new(
            Id: Guid.NewGuid(),
            Name: "Offset test",
            BaseStamina: 97,
            MaxStamina: 100,
            RecoveryMinutes: 7,
            RecordedAtUtc: recordedAt,
            ImageAssetId: null,
            SortOrder: 0);
        DateTimeOffset now = recordedAt
            .ToOffset(TimeSpan.FromHours(7))
            .AddMinutes(5);
        DateTimeOffset expectedFullAtUtc = recordedAt
            .ToUniversalTime()
            .AddMinutes(21);

        StaminaSnapshot snapshot =
            StaminaCalculator.Calculate(entry, now);

        Assert.IsNotNull(snapshot.FullAtUtc);
        Assert.AreEqual(TimeSpan.Zero, snapshot.FullAtUtc.Value.Offset);
        Assert.AreEqual(expectedFullAtUtc, snapshot.FullAtUtc.Value);
        Assert.AreEqual(TimeSpan.FromMinutes(16), snapshot.Remaining);
    }

    private static GameEntry CreateEntry(
        int baseStamina,
        int maxStamina,
        int recoveryMinutes)
    {
        return new GameEntry(
            Id: Guid.NewGuid(),
            Name: "Test game",
            BaseStamina: baseStamina,
            MaxStamina: maxStamina,
            RecoveryMinutes: recoveryMinutes,
            RecordedAtUtc: RecordedAtUtc,
            ImageAssetId: null,
            SortOrder: 0);
    }

    private static void AssertFullTimeOutOfRangeException(Action action)
    {
        ArgumentOutOfRangeException exception =
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(action);

        Assert.AreEqual("entry", exception.ParamName);
        Assert.IsTrue(
            exception.Message.StartsWith(
                FullTimeOutOfRangeMessage,
                StringComparison.Ordinal),
            $"Unexpected exception message: {exception.Message}");
    }
}
