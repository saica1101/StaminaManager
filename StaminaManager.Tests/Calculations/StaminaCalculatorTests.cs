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
    [DataRow(0, 1, 0, 0)]
    [DataRow(0, 1, 1, 1)]
    [DataRow(0, 1, 2, 2)]
    [DataRow(0, 59, 58, 0)]
    [DataRow(0, 59, 59, 1)]
    [DataRow(0, 59, 60, 1)]
    [DataRow(1, 0, 59, 0)]
    [DataRow(1, 0, 60, 1)]
    [DataRow(1, 0, 61, 1)]
    [DataRow(8, 30, 509, 0)]
    [DataRow(8, 30, 510, 1)]
    [DataRow(8, 30, 511, 1)]
    [DataRow(525_600, 0, 31_535_999, 0)]
    [DataRow(525_600, 0, 31_536_000, 1)]
    public void Calculate_RecoversAtWholeSecondIntervals(
        int recoveryMinutes,
        int recoverySeconds,
        int elapsedSeconds,
        int expectedRecovery)
    {
        GameEntry entry = CreateEntry(
            baseStamina: 0,
            maxStamina: 100,
            recoveryMinutes,
            recoverySeconds);

        StaminaSnapshot snapshot = StaminaCalculator.Calculate(
            entry,
            RecordedAtUtc.AddSeconds(elapsedSeconds));

        Assert.AreEqual(expectedRecovery, snapshot.Current);
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
            recoveryMinutes: 0,
            recoverySeconds: 1);

        StaminaSnapshot snapshot = StaminaCalculator.Calculate(
            entry,
            RecordedAtUtc.AddMinutes(-30));

        Assert.AreEqual(40, snapshot.Current);
    }

    [TestMethod]
    public void Calculate_ReturnsExactFullTimeForMaximumRecoveryInterval()
    {
        GameEntry entry = CreateEntry(
            baseStamina: 0,
            maxStamina: 100,
            recoveryMinutes: 525_600);
        DateTimeOffset expectedFullAtUtc =
            RecordedAtUtc.AddMinutes(100L * 525_600);

        StaminaSnapshot snapshot =
            StaminaCalculator.Calculate(entry, RecordedAtUtc);

        Assert.AreEqual(expectedFullAtUtc, snapshot.FullAtUtc);
        Assert.AreEqual(expectedFullAtUtc - RecordedAtUtc, snapshot.Remaining);
    }

    [TestMethod]
    public void Calculate_IncludesRecoverySecondsInFullTime()
    {
        GameEntry entry = CreateEntry(
            baseStamina: 0,
            maxStamina: 2,
            recoveryMinutes: 8,
            recoverySeconds: 30);

        StaminaSnapshot snapshot =
            StaminaCalculator.Calculate(entry, RecordedAtUtc);

        Assert.AreEqual(
            RecordedAtUtc.AddMinutes(17),
            snapshot.FullAtUtc);
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
            SortOrder: 0,
            RecoverySeconds: 0,
            IsNotificationEnabled: true);

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
            SortOrder: 0,
            RecoverySeconds: 59,
            IsNotificationEnabled: true);

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
            SortOrder: 0,
            RecoverySeconds: 0,
            IsNotificationEnabled: true);
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
        int recoveryMinutes,
        int recoverySeconds = 0)
    {
        return new GameEntry(
            Id: Guid.NewGuid(),
            Name: "Test game",
            BaseStamina: baseStamina,
            MaxStamina: maxStamina,
            RecoveryMinutes: recoveryMinutes,
            RecordedAtUtc: RecordedAtUtc,
            ImageAssetId: null,
            SortOrder: 0,
            RecoverySeconds: recoverySeconds,
            IsNotificationEnabled: true);
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
