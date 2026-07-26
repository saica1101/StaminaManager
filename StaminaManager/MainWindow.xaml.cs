using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;
using System.Runtime.InteropServices;

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
    private bool _isExplicitExit;
    private bool _isLifecycleConfigured;
    private readonly MainWindowContent _windowContent;

    public MainWindow(MainPage mainPage)
    {
        ArgumentNullException.ThrowIfNull(mainPage);
        _windowContent = new MainWindowContent();
        Content = _windowContent;
        SystemBackdrop = new MicaBackdrop();

        string appTitle = ResolveAppTitle();
        Title = appTitle;
        _windowContent.TitleBarControl.Title = appTitle;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(_windowContent.TitleBarControl);
        AppWindow.SetIcon("Assets/AppIcon.ico");
        _windowContent.RootFrameControl.Content = mainPage;
        UpdateBackdropDiagnostic();
    }

    internal void ConfigureLifecycle(
        IWindowStateService windowStateService,
        ITrayService trayService,
        Func<CloseBehavior> getCloseBehavior)
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

    private void OnAppWindowClosing(
        AppWindow sender,
        AppWindowClosingEventArgs args)
    {
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

        DisposeLifecycle();
    }

    private void OnTrayOpenRequested(object? sender, EventArgs args) =>
        RestoreAndActivate();

    private void OnTrayExitRequested(object? sender, EventArgs args)
    {
        if (_isExplicitExit)
        {
            return;
        }

        _isExplicitExit = true;
        if (!DispatcherQueue.TryEnqueue(CompleteExplicitExit))
        {
            CompleteExplicitExit();
        }
    }

    private void CompleteExplicitExit()
    {
        _windowStateService?.CaptureCurrent();
        DisposeLifecycle();
        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    private void DisposeLifecycle()
    {
        AppWindow.Closing -= OnAppWindowClosing;
        if (_trayService is not null)
        {
            _trayService.OpenRequested -= OnTrayOpenRequested;
            _trayService.ExitRequested -= OnTrayExitRequested;
            _trayService.Dispose();
        }
    }

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
