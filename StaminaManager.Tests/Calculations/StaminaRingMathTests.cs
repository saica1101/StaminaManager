using StaminaManager.Core.Calculations;

namespace StaminaManager.Tests.Calculations;

[TestClass]
public sealed class StaminaRingMathTests
{
    [TestMethod]
    [DataRow(-1d, 0d)]
    [DataRow(0d, 0d)]
    [DataRow(0.25d, 90d)]
    [DataRow(1d, 359.99d)]
    [DataRow(2d, 359.99d)]
    public void GetSweepDegrees_ClampsToOneVisibleRevolution(
        double ratio,
        double expected)
    {
        double actual = StaminaRingMath.GetSweepDegrees(ratio);

        Assert.AreEqual(expected, actual, 0.000_000_001);
        Assert.IsGreaterThanOrEqualTo(0d, actual);
        Assert.IsLessThanOrEqualTo(359.99d, actual);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(double.PositiveInfinity)]
    public void GetSweepDegrees_RejectsNonFiniteRatio(double ratio)
    {
        ArgumentOutOfRangeException exception =
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => StaminaRingMath.GetSweepDegrees(ratio));

        Assert.AreEqual("ratio", exception.ParamName);
    }
}
