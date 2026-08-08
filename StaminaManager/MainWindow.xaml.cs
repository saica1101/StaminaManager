using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Windows;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Windows.UI;

namespace StaminaManager;

/// <summary>
/// アプリケーションウィンドウ。表示内容はMainPageが管理する。
/// </summary>
public sealed partial class MainWindowContent : Page
{
    public MainWindowContent()
    {
        InitializeComponent();
    }

    internal TitleBar TitleBarControl => AppTitleBar;

    internal Frame RootFrameControl => RootFrame;

    internal Grid SolidBackdropSurfaceControl => SolidBackdropSurface;

    internal TextBlock BackdropDiagnosticControl =>
        ActualBackdropDiagnostic;
}

public sealed class MainWindow : WinUIEx.WindowEx
{
    private const string AppTitleResourceId = "AppTitle";
    private const string FallbackAppTitle = "Stamina Manager";
    private IWindowStateService? _windowStateService;
    private ITrayService? _trayService;
    private Func<CloseBehavior>? _getCloseBehavior;
    private Func<Task>? _shutdownAction;
    private ShutdownSequence? _shutdownSequence;
    private bool _isExplicitExit;
    private bool _isLifecycleConfigured;
    private bool _isLifecycleDisposed;
    private readonly MainWindowContent _windowContent;
    private readonly CoalescingUiAction _captionColorUpdate;

    public MainWindow(MainPage mainPage)
    {
        ArgumentNullException.ThrowIfNull(mainPage);
        _windowContent = new MainWindowContent();
        Content = _windowContent;
        _captionColorUpdate = new CoalescingUiAction(
            new DispatcherQueueUiWorkQueue(DispatcherQueue),
            ApplyCaptionButtonColors);
        SystemBackdrop = new MicaBackdrop();

        string appTitle = ResolveAppTitle();
        Title = appTitle;
        _windowContent.TitleBarControl.Title = appTitle;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_windowContent.TitleBarControl);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        _windowContent.RootFrameControl.Content = mainPage;
        UpdateBackdropDiagnostic();

        _windowContent.ActualThemeChanged += OnActualThemeChanged;
        ApplyCaptionButtonColors();
    }

    private void OnActualThemeChanged(
        FrameworkElement sender,
        object args)
    {
        if (!_captionColorUpdate.Request())
        {
            Debug.WriteLine(
                "タイトルバー色の更新をキューへ投入できませんでした。");
        }
    }

    private void ApplyCaptionButtonColors()
    {
        try
        {
            if (!AppWindowTitleBar.IsCustomizationSupported())
            {
                return;
            }

            AppWindowTitleBar titleBar = AppWindow.TitleBar;
            bool isDark =
                _windowContent.ActualTheme == ElementTheme.Dark;

            Windows.UI.Color foreground = isDark
                ? Color.FromArgb(255, 255, 255, 255)
                : Color.FromArgb(255, 26, 26, 26);
            Windows.UI.Color inactiveForeground = isDark
                ? Color.FromArgb(255, 207, 207, 207)
                : Color.FromArgb(255, 93, 93, 93);
            Windows.UI.Color hoverBackground = isDark
                ? Color.FromArgb(40, 255, 255, 255)
                : Color.FromArgb(20, 0, 0, 0);
            Windows.UI.Color pressedBackground = isDark
                ? Color.FromArgb(60, 255, 255, 255)
                : Color.FromArgb(35, 0, 0, 0);

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = foreground;
            titleBar.ButtonInactiveForegroundColor = inactiveForeground;
            titleBar.ButtonHoverBackgroundColor = hoverBackground;
            titleBar.ButtonHoverForegroundColor = foreground;
            titleBar.ButtonPressedBackgroundColor = pressedBackground;
            titleBar.ButtonPressedForegroundColor = foreground;
        }
        catch (Exception exception) when (
            exception is COMException
                or ArgumentException
                or InvalidOperationException)
        {
            Debug.WriteLine(
                "タイトルバー色の更新に失敗しました: "
                + exception.GetType().Name);
        }
    }

    internal void ConfigureLifecycle(
        IWindowStateService windowStateService,
        ITrayService trayService,
        Func<CloseBehavior> getCloseBehavior,
        Func<Task>? shutdownAction = null)
    {
        ArgumentNullException.ThrowIfNull(windowStateService);
        ArgumentNullException.ThrowIfNull(trayService);
        ArgumentNullException.ThrowIfNull(getCloseBehavior);
        if (_isLifecycleConfigured)
        {
            throw new InvalidOperationException(
                "ウィンドウのライフサイクルは既に構成されています。");
        }

        _windowStateService = windowStateService;
        _trayService = trayService;
        _getCloseBehavior = getCloseBehavior;
        _shutdownAction = shutdownAction;
        _shutdownSequence = new ShutdownSequence(
            DisposeLifecycleAsync,
            ReportShutdownFailure,
            () => Microsoft.UI.Xaml.Application.Current.Exit());
        AppWindow.Closing += OnAppWindowClosing;
        trayService.OpenRequested += OnTrayOpenRequested;
        trayService.ExitRequested += OnTrayExitRequested;
        _isLifecycleConfigured = true;
    }

    internal void RestoreAndActivate() => _trayService?.ShowWindow();

    internal void SetBackdrop(
        SystemBackdrop? systemBackdrop,
        bool isSolidSurface)
    {
        SystemBackdrop = systemBackdrop;
        _windowContent.SolidBackdropSurfaceControl.Visibility = isSolidSurface
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateBackdropDiagnostic();
    }

    internal string GetActualBackdropDiagnostic()
    {
        string backdrop = SystemBackdrop switch
        {
            MicaBackdrop => "Mica",
            AdjustableAcrylicBackdrop adjustableAcrylic =>
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Acrylic|TintOpacity={adjustableAcrylic.TintOpacityPercent / 100f:F2}"),
            DesktopAcrylicBackdrop => "Acrylic",
            Infrastructure.Windows.BlurredBackdrop => "Blur",
            WinUIEx.TransparentTintBackdrop => "Transparent",
            null when _windowContent.SolidBackdropSurfaceControl.Visibility
                == Visibility.Visible => "Solid",
            null => "None",
            _ => "Unknown",
        };
        return $"{backdrop}|SolidSurface="
            + _windowContent.SolidBackdropSurfaceControl.Visibility;
    }

    private async void OnAppWindowClosing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
        ShutdownSequence shutdownSequence = GetShutdownSequence();
        if (shutdownSequence.IsCompleted)
        {
            return;
        }

        if (shutdownSequence.IsRequested)
        {
            args.Cancel = true;
            return;
        }

        _windowStateService?.CaptureCurrent();
        CloseBehavior closeBehavior = _getCloseBehavior?.Invoke()
            ?? CloseBehavior.MinimizeToTray;
        if (WindowClosePolicy.ShouldMinimizeToTray(
            closeBehavior,
            _isExplicitExit))
        {
            args.Cancel = true;
            _trayService?.HideWindow();
            return;
        }

        args.Cancel = true;
        _isExplicitExit = true;
        await shutdownSequence.RequestAsync();
    }

    private void OnTrayOpenRequested(object? sender, EventArgs args) =>
        RestoreAndActivate();

    private async void OnTrayExitRequested(
        object? sender,
        EventArgs args)
    {
        ShutdownSequence shutdownSequence = GetShutdownSequence();
        if (shutdownSequence.IsRequested)
        {
            return;
        }

        _isExplicitExit = true;
        await RequestShutdownOnDispatcherAsync(shutdownSequence);
    }

    private Task RequestShutdownOnDispatcherAsync(
        ShutdownSequence shutdownSequence)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            _windowStateService?.CaptureCurrent();
            return shutdownSequence.RequestAsync();
        }

        TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        bool isQueued = DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                _windowStateService?.CaptureCurrent();
                await shutdownSequence.RequestAsync();
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });
        return isQueued
            ? completion.Task
            : shutdownSequence.RequestAsync();
    }

    private async Task DisposeLifecycleAsync()
    {
        if (_isLifecycleDisposed)
        {
            return;
        }

        try
        {
            if (_shutdownAction is not null)
            {
                await _shutdownAction();
            }
        }
        finally
        {
            try
            {
                AppWindow.Closing -= OnAppWindowClosing;
                if (_trayService is not null)
                {
                    _trayService.OpenRequested -= OnTrayOpenRequested;
                    _trayService.ExitRequested -= OnTrayExitRequested;
                    _trayService.Dispose();
                }
            }
            finally
            {
                _isLifecycleDisposed = true;
            }
        }
    }

    private ShutdownSequence GetShutdownSequence() =>
        _shutdownSequence
        ?? throw new InvalidOperationException(
            "ウィンドウのライフサイクルが構成されていません。");

    private static void ReportShutdownFailure(Exception exception) =>
        System.Diagnostics.Debug.WriteLine(
            "Application shutdown cleanup failed: "
            + exception.GetType().Name);

    private void UpdateBackdropDiagnostic()
    {
        string diagnostic = GetActualBackdropDiagnostic();
        _windowContent.BackdropDiagnosticControl.Text = diagnostic;
        AutomationProperties.SetName(
            _windowContent.BackdropDiagnosticControl,
            diagnostic);
    }

    private static string ResolveAppTitle()
    {
        try
        {
            string title = new ResourceLoader().GetString(
                AppTitleResourceId);
            return string.IsNullOrWhiteSpace(title)
                ? FallbackAppTitle
                : title;
        }
        catch (Exception exception) when (
            exception is COMException
                or ArgumentException
                or InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine(
                "タイトル リソースの解決に失敗しました: "
                + exception.GetType().Name);
            return FallbackAppTitle;
        }
    }
}
