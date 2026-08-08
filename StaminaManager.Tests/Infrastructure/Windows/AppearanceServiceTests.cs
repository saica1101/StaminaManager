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
    public void BackdropService_AppliesSelectableBackdropTypes()
    {
        RecordingBackdropTarget target = new();
        MutableBackdropEnvironment environment = new();
        BackdropService service = new(target, environment);

        AssertBackdrop<MicaBackdrop>(
            service,
            target,
            BackdropKind.Mica);
        BackdropResult acrylicResult = service.Apply(
            Request(BackdropKind.Acrylic));
        Assert.AreEqual(
            typeof(AdjustableAcrylicBackdrop),
            target.BackdropType);
        Assert.AreEqual(80, acrylicResult.ActualAcrylicTintOpacityPercent);
        BackdropResult solidResult = service.Apply(Request(BackdropKind.Solid));
        Assert.IsTrue(solidResult.IsRequestedBackdropApplied);
        Assert.IsNull(solidResult.ActualAcrylicTintOpacityPercent);
        Assert.IsNull(target.BackdropType);
        Assert.IsTrue(target.IsSolidSurface);
    }

    [STATestMethod]
    [DataRow(BackdropKind.Blur)]
    [DataRow(BackdropKind.Transparent)]
    public void BackdropService_旧背景はAcrylicとして適用する(
        BackdropKind legacyBackdrop)
    {
        RecordingBackdropTarget target = new();
        BackdropService service = new(
            target,
            new MutableBackdropEnvironment());

        BackdropResult result = service.Apply(Request(legacyBackdrop));

        Assert.AreEqual(BackdropKind.Acrylic, result.RequestedBackdrop);
        Assert.AreEqual(BackdropKind.Acrylic, result.ActualBackdrop);
        Assert.AreEqual(typeof(AdjustableAcrylicBackdrop), target.BackdropType);
        Assert.IsFalse(target.IsSolidSurface);
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

        BackdropResult result = service.Apply(Request(BackdropKind.Acrylic));

        Assert.AreEqual(BackdropKind.Acrylic, result.RequestedBackdrop);
        Assert.AreEqual(BackdropKind.Solid, result.ActualBackdrop);
        Assert.AreEqual(
            BackdropFallbackReason.HighContrast,
            result.FallbackReason);
        Assert.IsNull(result.ActualAcrylicTintOpacityPercent);
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

        BackdropResult result = service.Apply(Request(BackdropKind.Acrylic));

        Assert.AreEqual(BackdropKind.Solid, result.ActualBackdrop);
        Assert.AreEqual(expectedReason, result.FallbackReason);
        Assert.IsNull(result.ActualAcrylicTintOpacityPercent);
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

        BackdropResult result = service.Apply(Request(BackdropKind.Mica));

        Assert.AreEqual(BackdropKind.Solid, result.ActualBackdrop);
        Assert.AreEqual(
            BackdropFallbackReason.ApplyFailed,
            result.FallbackReason);
        Assert.IsNull(target.BackdropType);
        Assert.IsTrue(target.IsSolidSurface);
        Assert.IsNotNull(result.ErrorMessage);
        Assert.IsNull(result.ActualAcrylicTintOpacityPercent);
    }

    private static void AssertBackdrop<TBackdrop>(
        BackdropService service,
        RecordingBackdropTarget target,
        BackdropKind requested)
        where TBackdrop : XamlSystemBackdrop
    {
        BackdropResult result = service.Apply(Request(requested));

        Assert.IsTrue(
            result.IsRequestedBackdropApplied,
            result.ErrorMessage);
        Assert.AreEqual(typeof(TBackdrop), target.BackdropType);
        Assert.IsFalse(target.IsSolidSurface);
    }

    [STATestMethod]
    [DataRow(0, 0.0f)]
    [DataRow(50, 0.5f)]
    [DataRow(100, 1.0f)]
    public void BackdropService_AcrylicRequestは色調不透明度を適用する(
        int percent,
        float expectedTintOpacity)
    {
        RecordingBackdropTarget target = new();
        BackdropService service = new(
            target,
            new MutableBackdropEnvironment());

        BackdropResult result = service.Apply(
            Request(BackdropKind.Acrylic, percent));

        Assert.AreEqual(percent, result.ActualAcrylicTintOpacityPercent);
        Assert.AreEqual(expectedTintOpacity, percent / 100f);
    }

    [STATestMethod]
    public void BackdropService_同一Acrylicの色調不透明度更新で再生成しない()
    {
        RecordingBackdropTarget target = new();
        BackdropService service = new(
            target,
            new MutableBackdropEnvironment());

        service.Apply(Request(BackdropKind.Acrylic, 50));
        BackdropResult result = service.Apply(
            Request(BackdropKind.Acrylic, 100));

        Assert.AreEqual(1, target.SetBackdropCallCount);
        Assert.AreEqual(1, target.UpdateAcrylicTintOpacityCallCount);
        Assert.AreEqual(100, result.ActualAcrylicTintOpacityPercent);
    }

    [STATestMethod]
    public void BackdropService_Acrylic色調更新失敗時はSolidへフォールバックする()
    {
        RecordingBackdropTarget target = new();
        BackdropService service = new(
            target,
            new MutableBackdropEnvironment());
        _ = service.Apply(Request(BackdropKind.Acrylic, 50));
        target.ShouldThrowWhenUpdatingAcrylic = true;

        BackdropResult result = service.Apply(
            Request(BackdropKind.Acrylic, 100));

        Assert.AreEqual(BackdropKind.Solid, result.ActualBackdrop);
        Assert.AreEqual(
            BackdropFallbackReason.ApplyFailed,
            result.FallbackReason);
        Assert.IsNull(result.ActualAcrylicTintOpacityPercent);
    }

    [TestMethod]
    public void BackdropService_MicaとSolidの実色調不透明度はNull()
    {
        RecordingBackdropTarget target = new();
        BackdropService service = new(
            target,
            new MutableBackdropEnvironment());

        BackdropResult mica = service.Apply(Request(BackdropKind.Mica, 0));
        BackdropResult solid = service.Apply(Request(BackdropKind.Solid, 100));

        Assert.IsNull(mica.ActualAcrylicTintOpacityPercent);
        Assert.IsNull(solid.ActualAcrylicTintOpacityPercent);
    }

    private static BackdropRequest Request(
        BackdropKind backdrop,
        int tintOpacityPercent = 80) => new(backdrop, tintOpacityPercent);

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

        public bool ShouldThrowWhenUpdatingAcrylic { get; set; }

        public int SetBackdropCallCount { get; private set; }

        public int UpdateAcrylicTintOpacityCallCount { get; private set; }

        public void SetBackdrop(BackdropDefinition definition)
        {
            if (ShouldThrowOnce)
            {
                ShouldThrowOnce = false;
                throw new InvalidOperationException("apply failure");
            }

            SetBackdropCallCount++;
            BackdropType = definition.BackdropType;
            IsSolidSurface = definition.IsSolidSurface;
        }

        public bool TryUpdateAcrylicTintOpacity(int tintOpacityPercent)
        {
            if (ShouldThrowWhenUpdatingAcrylic)
            {
                throw new InvalidOperationException("apply failure");
            }

            if (BackdropType != typeof(AdjustableAcrylicBackdrop))
            {
                return false;
            }

            UpdateAcrylicTintOpacityCallCount++;
            return true;
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
