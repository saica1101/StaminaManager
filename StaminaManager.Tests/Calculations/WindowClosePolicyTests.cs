using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;

namespace StaminaManager.Tests.Calculations;

[TestClass]
public sealed class WindowClosePolicyTests
{
    [TestMethod]
    [DataRow(CloseBehavior.MinimizeToTray, false, true)]
    [DataRow(CloseBehavior.MinimizeToTray, true, false)]
    [DataRow(CloseBehavior.Exit, false, false)]
    [DataRow(CloseBehavior.Exit, true, false)]
    public void ShouldMinimizeToTray_明示終了guardと保存設定を判定する(
        CloseBehavior closeBehavior,
        bool isExplicitExit,
        bool expected)
    {
        bool actual = WindowClosePolicy.ShouldMinimizeToTray(
            closeBehavior,
            isExplicitExit);

        Assert.AreEqual(expected, actual);
    }
}
