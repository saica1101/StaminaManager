using StaminaManager.Core.Calculations;

namespace StaminaManager.Tests.Calculations;

[TestClass]
public sealed class GameCardLayoutPolicyTests
{
    [TestMethod]
    [DataRow(200d, true)]
    [DataRow(232d, true)]
    [DataRow(271.999d, true)]
    [DataRow(272d, false)]
    [DataRow(273d, false)]
    public void ShouldUseNarrowLayout_PreservesInformationWidth(
        double cardWidth,
        bool expected)
    {
        bool actual = GameCardLayoutPolicy.ShouldUseNarrowLayout(cardWidth);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(-1d)]
    public void ShouldUseNarrowLayout_RejectsInvalidWidth(double cardWidth)
    {
        ArgumentOutOfRangeException exception =
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(
                () => GameCardLayoutPolicy.ShouldUseNarrowLayout(cardWidth));

        Assert.AreEqual("cardWidth", exception.ParamName);
    }
}
