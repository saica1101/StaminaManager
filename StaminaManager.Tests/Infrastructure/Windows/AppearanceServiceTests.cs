using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Windows;
using System.Reflection;
using System.Runtime.InteropServices;
using WinUIEx;
using XamlSystemBackdrop = Microsoft.UI.Xaml.Media.SystemBackdrop;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class AppearanceServiceTests
{
    [TestMethod]
    public void Program_CoWaitForMultipleObjectsLoadsOnlyFromSystem32()
    {
        MethodInfo method = typeof(Program).GetMethod(
            "CoWaitForMultipleObjects",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new AssertFailedException(
                "CoWaitForMultipleObjects was not found.");

        DefaultDllImportSearchPathsAttribute? attribute =
            method.GetCustomAttribute<
                DefaultDllImportSearchPathsAttribute>();

        Assert.IsNotNull(attribute);
        Assert.AreEqual(
            DllImportSearchPath.System32,
            attribute.Paths);
    }

    [TestMethod]
    public void ThemeService_ResolvesWindowsThemeAndAppliesElementTheme()
    {
        RecordingThemeTarget target = new();
        ThemeService service = new(
            target,
            () => ApplicationTheme.Dark);

        AppTheme initialTheme = service.ResolveInitialTheme();
        ThemeResult result = service.Apply(AppTheme.Dark);

        Assert.AreEqual(AppTheme.Dark, initialTheme);
        Assert.IsTrue(result.IsApplied);
        Assert.AreEqual(ElementTheme.Dark, target.RequestedTheme);
    }

    [TestMethod]
    public void ThemeService_ApplyExceptionRestoresLastGoodTheme()
    {
        RecordingThemeTarget target = new()
        {
            RequestedTheme = ElementTheme.Light,
            ShouldThrowOnce = true,
        };
        ThemeService service = new(
            target,
            () => ApplicationTheme.Light);

        ThemeResult result = service.Apply(AppTheme.Dark);

        Assert.IsFalse(result.IsApplied);
        Assert.AreEqual(AppTheme.Light, result.ActualTheme);
        Assert.AreEqual(ElementTheme.Light, target.RequestedTheme);
        Assert.IsNotNull(result.ErrorMessage);
    }

    [STATestMethod]
    public void BackdropService_AppliesEachConcreteBackdropType()
    {
        RecordingBackdropTarget target = new();
        MutableBackdropEnvironment environment = new();
        BackdropService service = new(target, environment);

        AssertBackdrop<MicaBackdrop>(
            service,
            target,
            BackdropKind.Mica);
        AssertBackdrop<DesktopAcrylicBackdrop>(
            service,
            target,
            BackdropKind.Acrylic);
        AssertBackdrop<BlurredBackdrop>(
            service,
            target,
            BackdropKind.Blur);
        AssertBackdrop<TransparentTintBackdrop>(
            service,
            target,
            BackdropKind.Transparent);

        BackdropResult solidResult = service.Apply(BackdropKind.Solid);
        Assert.IsTrue(solidResult.IsRequestedBackdropApplied);
        Assert.IsNull(target.BackdropType);
        Assert.IsTrue(target.IsSolidSurface);
    }

    [STATestMethod]
    public void BackdropService_HighContrastReturnsTypedSolidFallback()
    {
        RecordingBackdropTarget target = new();
        MutableBackdropEnvironment environment = new()
        {
            IsHighContrast = true,
        };
        BackdropService service = new(target, environment);

        BackdropResult result = service.Apply(BackdropKind.Transparent);

        Assert.AreEqual(BackdropKind.Transparent, result.RequestedBackdrop);
        Assert.AreEqual(BackdropKind.Solid, result.ActualBackdrop);
        Assert.AreEqual(
            BackdropFallbackReason.HighContrast,
            result.FallbackReason);
        Assert.IsNull(target.BackdropType);
        Assert.IsTrue(target.IsSolidSurface);
    }

    [TestMethod]
    [DataRow(false, false, true,
        BackdropFallbackReason.TransparencyDisabled)]
    [DataRow(true, true, true,
        BackdropFallbackReason.RemoteSession)]
    [DataRow(true, false, false,
        BackdropFallbackReason.Unsupported)]
    public void BackdropService_EnvironmentRestrictionReturnsTypedFallback(
        bool areTransparencyEffectsEnabled,
        bool isRemoteSession,
        bool isSupported,
        BackdropFallbackReason expectedReason)
    {
        RecordingBackdropTarget target = new();
        MutableBackdropEnvironment environment = new()
        {
            AreTransparencyEffectsEnabled =
                areTransparencyEffectsEnabled,
            IsRemoteSession = isRemoteSession,
            IsBackdropSupported = isSupported,
        };
        BackdropService service = new(target, environment);

        BackdropResult result = service.Apply(BackdropKind.Blur);

        Assert.AreEqual(BackdropKind.Solid, result.ActualBackdrop);
        Assert.AreEqual(expectedReason, result.FallbackReason);
        Assert.IsNull(target.BackdropType);
        Assert.IsTrue(target.IsSolidSurface);
    }

    [STATestMethod]
    public void BackdropService_ApplyExceptionReturnsTypedSolidFallback()
    {
        RecordingBackdropTarget target = new()
        {
            ShouldThrowOnce = true,
        };
        BackdropService service = new(
            target,
            new MutableBackdropEnvironment());

        BackdropResult result = service.Apply(BackdropKind.Mica);

        Assert.AreEqual(BackdropKind.Solid, result.ActualBackdrop);
        Assert.AreEqual(
            BackdropFallbackReason.ApplyFailed,
            result.FallbackReason);
        Assert.IsNull(target.BackdropType);
        Assert.IsTrue(target.IsSolidSurface);
        Assert.IsNotNull(result.ErrorMessage);
    }

    private static void AssertBackdrop<TBackdrop>(
        BackdropService service,
        RecordingBackdropTarget target,
        BackdropKind requested)
        where TBackdrop : XamlSystemBackdrop
    {
        BackdropResult result = service.Apply(requested);

        Assert.IsTrue(
            result.IsRequestedBackdropApplied,
            result.ErrorMessage);
        Assert.AreEqual(typeof(TBackdrop), target.BackdropType);
        Assert.IsFalse(target.IsSolidSurface);
    }

    private sealed class RecordingThemeTarget : IThemeTarget
    {
        private ElementTheme _requestedTheme = ElementTheme.Default;

        public bool ShouldThrowOnce { get; set; }

        public ElementTheme RequestedTheme
        {
            get => _requestedTheme;
            set
            {
                if (ShouldThrowOnce)
                {
                    ShouldThrowOnce = false;
                    throw new InvalidOperationException("apply failure");
                }

                _requestedTheme = value;
            }
        }
    }

    private sealed class RecordingBackdropTarget : IBackdropTarget
    {
        public Type? BackdropType { get; private set; }

        public bool IsSolidSurface { get; private set; }

        public bool ShouldThrowOnce { get; set; }

        public void SetBackdrop(BackdropDefinition definition)
        {
            if (ShouldThrowOnce)
            {
                ShouldThrowOnce = false;
                throw new InvalidOperationException("apply failure");
            }

            BackdropType = definition.BackdropType;
            IsSolidSurface = definition.IsSolidSurface;
        }
    }

    private sealed class MutableBackdropEnvironment : IBackdropEnvironment
    {
        public bool IsHighContrast { get; init; }

        public bool AreTransparencyEffectsEnabled { get; init; } = true;

        public bool IsRemoteSession { get; init; }

        public bool IsBackdropSupported { get; init; } = true;

        public bool IsSupported(BackdropKind backdrop) =>
            IsBackdropSupported;
    }
}
