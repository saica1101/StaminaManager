using StaminaManager.Core.Calculations;

namespace StaminaManager.Tests.Calculations;

[TestClass]
public sealed class RemainingTimePartsTests
{
    [TestMethod]
    public void From_RoundsPartialMinuteUp()
    {
        RemainingTimeParts actual = RemainingTimeParts.From(
            TimeSpan.FromHours(2)
                + TimeSpan.FromMinutes(3)
                + TimeSpan.FromSeconds(1));

        Assert.IsFalse(actual.IsFull);
        Assert.AreEqual(0, actual.Days);
        Assert.AreEqual(2, actual.Hours);
        Assert.AreEqual(4, actual.Minutes);
    }

    [TestMethod]
    public void From_UsesDayBasedPartsAtTwentyFourHours()
    {
        RemainingTimeParts actual = RemainingTimeParts.From(
            TimeSpan.FromDays(2)
                + TimeSpan.FromHours(3)
                + TimeSpan.FromMinutes(4));

        Assert.IsFalse(actual.IsFull);
        Assert.AreEqual(2, actual.Days);
        Assert.AreEqual(3, actual.Hours);
        Assert.AreEqual(4, actual.Minutes);
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
    }
}
