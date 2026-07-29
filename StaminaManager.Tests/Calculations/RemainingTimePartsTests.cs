using StaminaManager.Core.Calculations;

namespace StaminaManager.Tests.Calculations;

[TestClass]
public sealed class RemainingTimePartsTests
{
    [TestMethod]
    public void From_RoundsPartialSecondUpBelowOneDay()
    {
        RemainingTimeParts actual = RemainingTimeParts.From(
            TimeSpan.FromMilliseconds(1_100));

        Assert.IsFalse(actual.IsFull);
        Assert.AreEqual(0, actual.Days);
        Assert.AreEqual(0, actual.Hours);
        Assert.AreEqual(0, actual.Minutes);
        Assert.AreEqual(2, actual.Seconds);
    }

    [TestMethod]
    public void From_UsesHourMinuteSecondPartsBelowOneDay()
    {
        RemainingTimeParts actual = RemainingTimeParts.From(
            TimeSpan.FromHours(1)
                + TimeSpan.FromMinutes(2)
                + TimeSpan.FromSeconds(3));

        Assert.IsFalse(actual.IsFull);
        Assert.AreEqual(0, actual.Days);
        Assert.AreEqual(1, actual.Hours);
        Assert.AreEqual(2, actual.Minutes);
        Assert.AreEqual(3, actual.Seconds);
    }

    [TestMethod]
    public void From_KeepsRoundedTwentyFourHoursBelowOneDay()
    {
        RemainingTimeParts actual = RemainingTimeParts.From(
            TimeSpan.FromHours(23)
                + TimeSpan.FromMinutes(59)
                + TimeSpan.FromSeconds(59.1));

        Assert.IsFalse(actual.IsFull);
        Assert.AreEqual(0, actual.Days);
        Assert.AreEqual(24, actual.Hours);
        Assert.AreEqual(0, actual.Minutes);
        Assert.AreEqual(0, actual.Seconds);
    }

    [TestMethod]
    public void From_UsesDayBasedPartsAtExactlyTwentyFourHours()
    {
        RemainingTimeParts actual = RemainingTimeParts.From(
            TimeSpan.FromDays(1));

        Assert.IsFalse(actual.IsFull);
        Assert.AreEqual(1, actual.Days);
        Assert.AreEqual(0, actual.Hours);
        Assert.AreEqual(0, actual.Minutes);
        Assert.AreEqual(0, actual.Seconds);
    }

    [TestMethod]
    public void From_UsesDayBasedPartsAboveTwentyFourHours()
    {
        RemainingTimeParts actual = RemainingTimeParts.From(
            TimeSpan.FromDays(2)
                + TimeSpan.FromHours(3)
                + TimeSpan.FromMinutes(4));

        Assert.IsFalse(actual.IsFull);
        Assert.AreEqual(2, actual.Days);
        Assert.AreEqual(3, actual.Hours);
        Assert.AreEqual(4, actual.Minutes);
        Assert.AreEqual(0, actual.Seconds);
    }

    [TestMethod]
    public void From_RoundsPartialMinuteUpAtOrAboveOneDay()
    {
        RemainingTimeParts actual = RemainingTimeParts.From(
            TimeSpan.FromDays(1) + TimeSpan.FromSeconds(1));

        Assert.IsFalse(actual.IsFull);
        Assert.AreEqual(1, actual.Days);
        Assert.AreEqual(0, actual.Hours);
        Assert.AreEqual(1, actual.Minutes);
        Assert.AreEqual(0, actual.Seconds);
    }

    [TestMethod]
    [DataRow(0d)]
    [DataRow(-1d)]
    public void From_TreatsNonPositiveDurationAsFull(double minutes)
    {
        RemainingTimeParts actual = RemainingTimeParts.From(
            TimeSpan.FromMinutes(minutes));

        Assert.IsTrue(actual.IsFull);
        Assert.AreEqual(0, actual.Days);
        Assert.AreEqual(0, actual.Hours);
        Assert.AreEqual(0, actual.Minutes);
        Assert.AreEqual(0, actual.Seconds);
    }
}
