using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.System;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.Foundation.Metadata;
using Windows.UI.ViewManagement;
using WinUIEx;
using XamlSystemBackdrop = Microsoft.UI.Xaml.Media.SystemBackdrop;

namespace StaminaManager.Infrastructure.Windows;

public interface IBackdropTarget
{
    void SetBackdrop(BackdropDefinition definition);
}

public sealed record BackdropDefinition(
    Type? BackdropType,
    Func<XamlSystemBackdrop?> Create,
    bool IsSolidSurface);

public interface IBackdropEnvironment
{
    bool IsHighContrast { get; }

    bool AreTransparencyEffectsEnabled { get; }

    bool IsRemoteSession { get; }

    bool IsSupported(BackdropKind backdrop);
}

public sealed class MainWindowBackdropTarget(
    Func<MainWindow?> windowAccessor) : IBackdropTarget
{
    public void SetBackdrop(BackdropDefinition definition)
    {
        MainWindow window = windowAccessor()
            ?? throw new InvalidOperationException(
                "バックドロップの適用先がまだ作成されていません。");
        XamlSystemBackdrop? systemBackdrop = definition.Create();
        window.SetBackdrop(
            systemBackdrop,
            definition.IsSolidSurface);
    }
}

public sealed class WindowsBackdropEnvironment(
    Func<WindowId?> windowIdAccessor) : IBackdropEnvironment
{
    private const int SmRemoteSession = 0x1000;

    public bool IsHighContrast
    {
        get
        {
            try
            {
                WindowId? windowId = windowIdAccessor();
                return windowId is null
                    || ThemeSettings.CreateForWindowId(windowId.Value)
                        .HighContrast;
            }
            catch (Exception exception) when (IsEnvironmentException(exception))
            {
                Debug.WriteLine(
                    "High contrast detection failed: "
                    + exception.GetType().Name);
                return true;
            }
        }
    }

    public bool AreTransparencyEffectsEnabled
    {
        get
        {
            try
            {
                return new UISettings().AdvancedEffectsEnabled;
            }
            catch (Exception exception) when (IsEnvironmentException(exception))
            {
                Debug.WriteLine(
                    "Transparency detection failed: "
                    + exception.GetType().Name);
                return false;
            }
        }
    }

    public bool IsRemoteSession => GetSystemMetrics(SmRemoteSession) != 0;

    public bool IsSupported(BackdropKind backdrop) => backdrop switch
    {
        BackdropKind.Mica => MicaController.IsSupported(),
        BackdropKind.Acrylic => DesktopAcrylicController.IsSupported(),
        BackdropKind.Blur or BackdropKind.Transparent =>
            ApiInformation.IsTypePresent(
                "Windows.UI.Composition.Compositor"),
        BackdropKind.Solid => true,
        _ => false,
    };

    private static bool IsEnvironmentException(Exception exception) =>
        exception is InvalidOperationException
            or ArgumentException
            or COMException;

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}

public sealed class BackdropService : IBackdropService
{
    private readonly IBackdropTarget _target;
    private readonly IBackdropEnvironment _environment;
    private BackdropKind _lastActualBackdrop = BackdropKind.Mica;

    public BackdropService(
        IBackdropTarget target,
        IBackdropEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(environment);
        _target = target;
        _environment = environment;
    }

    public BackdropResult Apply(BackdropKind requestedBackdrop)
    {
        if (!Enum.IsDefined(requestedBackdrop))
        {
            return ApplySolidFallback(
                requestedBackdrop,
                BackdropFallbackReason.Unsupported,
                "背景の設定値が正しくないため、単色背景を使用します。");
        }

        if (requestedBackdrop == BackdropKind.Solid)
        {
            return ApplyRequestedSolid();
        }

        BackdropResult? environmentFallback =
            GetEnvironmentFallback(requestedBackdrop);
        if (environmentFallback is not null)
        {
            return environmentFallback;
        }

        try
        {
            _target.SetBackdrop(CreateDefinition(requestedBackdrop));
            _lastActualBackdrop = requestedBackdrop;
            return new BackdropResult(
                requestedBackdrop,
                requestedBackdrop,
                BackdropFallbackReason.None,
                ErrorMessage: null);
        }
        catch (Exception exception) when (IsApplyException(exception))
        {
            Debug.WriteLine(
                "Backdrop apply failed: " + exception.GetType().Name);
            return ApplySolidFallback(
                requestedBackdrop,
                BackdropFallbackReason.ApplyFailed,
                "選択した背景を適用できないため、単色背景を使用します。");
        }
    }

    private BackdropResult ApplyRequestedSolid()
    {
        try
        {
            _target.SetBackdrop(CreateSolidDefinition());
            _lastActualBackdrop = BackdropKind.Solid;
            return new BackdropResult(
                BackdropKind.Solid,
                BackdropKind.Solid,
                BackdropFallbackReason.None,
                ErrorMessage: null);
        }
        catch (Exception exception) when (IsApplyException(exception))
        {
            Debug.WriteLine(
                "Solid backdrop apply failed: "
                + exception.GetType().Name);
            return new BackdropResult(
                BackdropKind.Solid,
                _lastActualBackdrop,
                BackdropFallbackReason.SolidFallbackFailed,
                "単色背景を適用できませんでした。アプリを再起動してください。");
        }
    }

    private BackdropResult? GetEnvironmentFallback(
        BackdropKind requestedBackdrop)
    {
        if (_environment.IsHighContrast)
        {
            return ApplySolidFallback(
                requestedBackdrop,
                BackdropFallbackReason.HighContrast,
                "ハイ コントラストでは単色背景を使用します。");
        }

        if (!_environment.AreTransparencyEffectsEnabled)
        {
            return ApplySolidFallback(
                requestedBackdrop,
                BackdropFallbackReason.TransparencyDisabled,
                "Windowsの透明効果が無効なため、単色背景を使用します。");
        }

        if (_environment.IsRemoteSession)
        {
            return ApplySolidFallback(
                requestedBackdrop,
                BackdropFallbackReason.RemoteSession,
                "リモート セッションでは単色背景を使用します。");
        }

        if (!_environment.IsSupported(requestedBackdrop))
        {
            return ApplySolidFallback(
                requestedBackdrop,
                BackdropFallbackReason.Unsupported,
                "この環境では選択した背景を使用できないため、単色背景を使用します。");
        }

        return null;
    }

    private BackdropResult ApplySolidFallback(
        BackdropKind requestedBackdrop,
        BackdropFallbackReason reason,
        string message)
    {
        try
        {
            _target.SetBackdrop(CreateSolidDefinition());
            _lastActualBackdrop = BackdropKind.Solid;
            return new BackdropResult(
                requestedBackdrop,
                BackdropKind.Solid,
                reason,
                message);
        }
        catch (Exception exception) when (IsApplyException(exception))
        {
            Debug.WriteLine(
                "Solid fallback failed: " + exception.GetType().Name);
            return new BackdropResult(
                requestedBackdrop,
                _lastActualBackdrop,
                BackdropFallbackReason.SolidFallbackFailed,
                message + " 単色背景も適用できないため、アプリを再起動してください。");
        }
    }

    private static BackdropDefinition CreateDefinition(
        BackdropKind backdrop) => backdrop switch
    {
        BackdropKind.Mica => new BackdropDefinition(
            typeof(Microsoft.UI.Xaml.Media.MicaBackdrop),
            static () => new Microsoft.UI.Xaml.Media.MicaBackdrop(),
            IsSolidSurface: false),
        BackdropKind.Acrylic => new BackdropDefinition(
            typeof(Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop),
            static () =>
                new Microsoft.UI.Xaml.Media.DesktopAcrylicBackdrop(),
            IsSolidSurface: false),
        BackdropKind.Blur => new BackdropDefinition(
            typeof(BlurredBackdrop),
            static () => new BlurredBackdrop(),
            IsSolidSurface: false),
        BackdropKind.Transparent => new BackdropDefinition(
            typeof(TransparentTintBackdrop),
            static () => new TransparentTintBackdrop(),
            IsSolidSurface: false),
        _ => throw new ArgumentOutOfRangeException(nameof(backdrop)),
    };

    private static BackdropDefinition CreateSolidDefinition() => new(
        BackdropType: null,
        static () => null,
        IsSolidSurface: true);

    private static bool IsApplyException(Exception exception) =>
        exception is InvalidOperationException
            or ArgumentException
            or COMException
            or NotSupportedException;
}
