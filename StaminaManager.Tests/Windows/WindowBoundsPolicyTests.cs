using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;

namespace StaminaManager.Tests.Windows;

[TestClass]
public sealed class WindowBoundsPolicyTests
{
    [TestMethod]
    public void GetProfile_通常とコンパクトで保存キーと制約を分ける()
    {
        WindowBoundsProfile standard = WindowBoundsPolicy.GetProfile(
            AppDisplayMode.Standard);
        WindowBoundsProfile compact = WindowBoundsPolicy.GetProfile(
            AppDisplayMode.Compact);

        Assert.AreEqual("StaminaManager.Standard", standard.PersistenceId);
        Assert.AreEqual(new WindowSize(1120, 760), standard.InitialSize);
        Assert.AreEqual(new WindowSize(520, 520), standard.MinimumSize);
        Assert.AreEqual("StaminaManager.Compact", compact.PersistenceId);
        Assert.AreEqual(new WindowSize(420, 520), compact.InitialSize);
        Assert.AreEqual(new WindowSize(360, 480), compact.MinimumSize);
        Assert.AreNotEqual(standard.PersistenceId, compact.PersistenceId);
    }

    [TestMethod]
    public void Restore_切断モニター上の位置をnearestWorkArea内へ補正する()
    {
        WindowBoundsSnapshot snapshot = new(
            new WindowBounds(5000, -1500, 900, 700),
            SavedDpi: 96);
        WindowWorkArea nearestWorkArea = new(0, 0, 1920, 1040);

        WindowBounds restored = WindowBoundsPolicy.Restore(
            AppDisplayMode.Standard,
            snapshot,
            nearestWorkArea,
            currentDpi: 96);

        Assert.AreEqual(new WindowBounds(1020, 0, 900, 700), restored);
    }

    [TestMethod]
    public void Restore_保存Dpiから現在Dpiへサイズを換算する()
    {
        WindowBoundsSnapshot snapshot = new(
            new WindowBounds(100, 120, 800, 600),
            SavedDpi: 96);
        WindowWorkArea workArea = new(0, 0, 2560, 1440);

        WindowBounds restored = WindowBoundsPolicy.Restore(
            AppDisplayMode.Standard,
            snapshot,
            workArea,
            currentDpi: 144);

        Assert.AreEqual(new WindowBounds(100, 120, 1200, 900), restored);
    }

    [TestMethod]
    [DataRow(AppDisplayMode.Standard, 144d, 780, 780)]
    [DataRow(AppDisplayMode.Compact, 96d, 360, 480)]
    public void Restore_表示モード別のeffective最小サイズを保証する(
        AppDisplayMode displayMode,
        double currentDpi,
        int expectedWidth,
        int expectedHeight)
    {
        WindowBoundsSnapshot snapshot = new(
            new WindowBounds(20, 30, 1, 1),
            SavedDpi: currentDpi);
        WindowWorkArea workArea = new(0, 0, 2560, 1440);

        WindowBounds restored = WindowBoundsPolicy.Restore(
            displayMode,
            snapshot,
            workArea,
            currentDpi);

        Assert.AreEqual(expectedWidth, restored.Width);
        Assert.AreEqual(expectedHeight, restored.Height);
    }

    [TestMethod]
    public void Restore_負の境界とゼロDpiを安全な範囲へ補正する()
    {
        WindowBoundsSnapshot snapshot = new(
            new WindowBounds(int.MinValue, int.MaxValue, -10, 0),
            SavedDpi: 0);
        WindowWorkArea workArea = new(-1920, -1080, 1920, 1080);

        WindowBounds restored = WindowBoundsPolicy.Restore(
            AppDisplayMode.Standard,
            snapshot,
            workArea,
            currentDpi: 0);

        Assert.AreEqual(520, restored.Width);
        Assert.AreEqual(520, restored.Height);
        Assert.IsGreaterThanOrEqualTo(workArea.X, restored.X);
        Assert.IsGreaterThanOrEqualTo(workArea.Y, restored.Y);
        Assert.IsLessThanOrEqualTo(
            workArea.X + workArea.Width,
            restored.X + restored.Width);
        Assert.IsLessThanOrEqualTo(
            workArea.Y + workArea.Height,
            restored.Y + restored.Height);
    }

    [TestMethod]
    public void Restore_極端なDpiでもoverflowせずworkArea内へ補正する()
    {
        WindowBoundsSnapshot snapshot = new(
            new WindowBounds(
                int.MaxValue,
                int.MinValue,
                int.MaxValue,
                int.MaxValue),
            SavedDpi: double.Epsilon);
        WindowWorkArea workArea = new(-2560, 0, 2560, 1400);

        WindowBounds restored = WindowBoundsPolicy.Restore(
            AppDisplayMode.Compact,
            snapshot,
            workArea,
            currentDpi: double.MaxValue);

        Assert.IsGreaterThan(0, restored.Width);
        Assert.IsGreaterThan(0, restored.Height);
        Assert.IsGreaterThanOrEqualTo(workArea.X, restored.X);
        Assert.IsGreaterThanOrEqualTo(workArea.Y, restored.Y);
        Assert.IsLessThanOrEqualTo(
            (long)workArea.X + workArea.Width,
            (long)restored.X + restored.Width);
        Assert.IsLessThanOrEqualTo(
            (long)workArea.Y + workArea.Height,
            (long)restored.Y + restored.Height);
    }
}
