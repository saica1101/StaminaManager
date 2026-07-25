using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;

namespace StaminaManager.Tests.Calculations;

[TestClass]
public sealed class WindowDisplayModePolicyTests
{
    [TestMethod]
    [DataRow(520, 96u, 520)]
    [DataRow(520, 144u, 780)]
    [DataRow(360, 192u, 720)]
    [DataRow(520, 0u, 520)]
    public void ScaleEffectiveToPhysical_現在のDpiに変換する(
        int effectivePixels,
        uint dpi,
        int expected)
    {
        int actual = WindowDisplayModePolicy.ScaleEffectiveToPhysical(
            effectivePixels,
            dpi);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void CaptureSnapshot_Restoredでは現在の通常boundsを保存する()
    {
        WindowBounds previous = new(10, 20, 900, 600);
        WindowBounds current = new(30, 40, 1120, 760);

        WindowDisplayModeSnapshot snapshot =
            WindowDisplayModePolicy.CaptureSnapshot(
                WindowPresenterState.Restored,
                current,
                previous);

        Assert.AreEqual(current, snapshot.RestoredBounds);
        Assert.IsFalse(snapshot.ShouldMaximizeOnReturn);
    }

    [TestMethod]
    public void CaptureSnapshot_Maximizedでは以前の通常boundsと最大化を保存する()
    {
        WindowBounds previous = new(10, 20, 900, 600);
        WindowBounds maximized = new(0, 0, 1920, 1080);

        WindowDisplayModeSnapshot snapshot =
            WindowDisplayModePolicy.CaptureSnapshot(
                WindowPresenterState.Maximized,
                maximized,
                previous);

        Assert.AreEqual(previous, snapshot.RestoredBounds);
        Assert.IsTrue(snapshot.ShouldMaximizeOnReturn);
    }

    [TestMethod]
    public void CaptureSnapshot_Minimizedでは以前の通常boundsをRestored扱いで保存する()
    {
        WindowBounds previous = new(10, 20, 900, 600);
        WindowBounds minimized = new(-32000, -32000, 160, 28);

        WindowDisplayModeSnapshot snapshot =
            WindowDisplayModePolicy.CaptureSnapshot(
                WindowPresenterState.Minimized,
                minimized,
                previous);

        Assert.AreEqual(previous, snapshot.RestoredBounds);
        Assert.IsFalse(snapshot.ShouldMaximizeOnReturn);
    }

    [TestMethod]
    [DataRow(AppDisplayMode.Standard, false, WindowPresenterState.Restored, true)]
    [DataRow(AppDisplayMode.Compact, false, WindowPresenterState.Restored, false)]
    [DataRow(AppDisplayMode.Standard, true, WindowPresenterState.Restored, false)]
    [DataRow(AppDisplayMode.Standard, false, WindowPresenterState.Maximized, false)]
    public void ShouldCaptureRestoredBounds_通常表示の安定したRestored時だけtrue(
        AppDisplayMode displayMode,
        bool isTransitioning,
        WindowPresenterState presenterState,
        bool expected)
    {
        bool actual = WindowDisplayModePolicy.ShouldCaptureRestoredBounds(
            displayMode,
            isTransitioning,
            presenterState);

        Assert.AreEqual(expected, actual);
    }
}
