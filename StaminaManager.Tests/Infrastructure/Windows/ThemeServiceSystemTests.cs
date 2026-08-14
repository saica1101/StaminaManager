using Microsoft.UI.Xaml;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class ThemeServiceSystemTests
{
    [TestMethod]
    public void Apply_SystemUsesDarkWindowsTheme()
    {
        RecordingThemeTarget target = new();
        ThemeService service = new(
            target,
            () => ApplicationTheme.Dark);

        var result = service.Apply(AppTheme.System);

        Assert.IsTrue(result.IsApplied);
        Assert.AreEqual(AppTheme.System, result.RequestedTheme);
        Assert.AreEqual(AppTheme.System, result.ActualTheme);
        Assert.AreEqual(ElementTheme.Dark, target.RequestedTheme);
    }

    [TestMethod]
    public void Apply_SystemUsesLightWindowsTheme()
    {
        RecordingThemeTarget target = new();
        ThemeService service = new(
            target,
            () => ApplicationTheme.Light);

        var result = service.Apply(AppTheme.System);

        Assert.IsTrue(result.IsApplied);
        Assert.AreEqual(AppTheme.System, result.ActualTheme);
        Assert.AreEqual(ElementTheme.Light, target.RequestedTheme);
    }

    [TestMethod]
    public void ResolveInitialTheme_UsesWindowsThemeAccessor()
    {
        ApplicationTheme windowsTheme = ApplicationTheme.Light;
        RecordingThemeTarget target = new();
        ThemeService service = new(target, () => windowsTheme);

        Assert.AreEqual(AppTheme.Light, service.ResolveInitialTheme());

        windowsTheme = ApplicationTheme.Dark;

        Assert.AreEqual(AppTheme.Dark, service.ResolveInitialTheme());
    }

    private sealed class RecordingThemeTarget : IThemeTarget
    {
        public ElementTheme RequestedTheme { get; set; } = ElementTheme.Default;
    }
}
