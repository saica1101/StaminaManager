using StaminaManager.Core.Calculations;

namespace StaminaManager.Tests.Calculations;

[TestClass]
public sealed class OverviewLayoutPolicyTests
{
    [TestMethod]
    [DataRow(840d, 3)]
    [DataRow(839d, 2)]
    [DataRow(560d, 2)]
    [DataRow(559d, 1)]
    [DataRow(0d, 1)]
    public void GetColumns_UsesSpecifiedBreakpoints(
        double width,
        int expected)
    {
        int actual = OverviewLayoutPolicy.GetColumns(width);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(-1d)]
    public void GetColumns_RejectsInvalidContentWidth(double width)
    {
        ArgumentOutOfRangeException exception =
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => OverviewLayoutPolicy.GetColumns(width));

        Assert.AreEqual("contentWidth", exception.ParamName);
    }
}
