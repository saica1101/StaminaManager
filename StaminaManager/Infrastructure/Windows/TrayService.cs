using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Resources;
using WinUIEx;

namespace StaminaManager.Infrastructure.Windows;

public sealed class TrayService : ITrayService
{
    private readonly ITrayPlatformAdapter _adapter;
    private bool _isInitialized;
    private bool _isDisposed;

    public TrayService(Func<MainWindow?> getWindow)
        : this(new WinUiExTrayPlatformAdapter(getWindow))
    {
    }

    internal TrayService(ITrayPlatformAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _adapter = adapter;
        _adapter.OpenRequested += OnOpenRequested;
        _adapter.ExitRequested += OnExitRequested;
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public event EventHandler<WindowVisibilityChangedEventArgs>?
        WindowVisibilityChanged;

    public void Initialize()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isInitialized)
        {
            return;
        }

        _adapter.Initialize();
        _isInitialized = true;
    }

    public void HideWindow()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _adapter.HideWindow();
        WindowVisibilityChanged?.Invoke(
            this,
            new WindowVisibilityChangedEventArgs(isShown: false));
    }

    public void ShowWindow()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        _adapter.ShowWindow();
        WindowVisibilityChanged?.Invoke(
            this,
            new WindowVisibilityChangedEventArgs(isShown: true));
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _adapter.OpenRequested -= OnOpenRequested;
        _adapter.ExitRequested -= OnExitRequested;
        _adapter.Dispose();
        _isDisposed = true;
    }

    private void OnOpenRequested(object? sender, EventArgs args) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnExitRequested(object? sender, EventArgs args) =>
        ExitRequested?.Invoke(this, EventArgs.Empty);
}

internal interface ITrayPlatformAdapter : IDisposable
{
    event EventHandler? OpenRequested;

    event EventHandler? ExitRequested;

    void Initialize();

    void HideWindow();

    void ShowWindow();
}

internal sealed class WinUiExTrayPlatformAdapter : ITrayPlatformAdapter
{
    private const uint TrayIconId = 1;
    private readonly Func<MainWindow?> _getWindow;
    private TrayIcon? _trayIcon;

    public WinUiExTrayPlatformAdapter(Func<MainWindow?> getWindow)
    {
        ArgumentNullException.ThrowIfNull(getWindow);
        _getWindow = getWindow;
    }

    public event EventHandler? OpenRequested;

    public event EventHandler? ExitRequested;

    public void Initialize()
    {
        if (_trayIcon is not null)
        {
            return;
        }

        string iconPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "AppIcon.ico");
        _trayIcon = new TrayIcon(
            TrayIconId,
            iconPath,
            "Stamina Manager");
        _trayIcon.LeftDoubleClick += OnOpenRequested;
        _trayIcon.ContextMenu += OnContextMenu;
        _trayIcon.IsVisible = true;
    }

    public void HideWindow()
    {
        MainWindow window = GetWindow();
        window.AppWindow.IsShownInSwitchers = false;
        window.AppWindow.Hide();
    }

    public void ShowWindow()
    {
        MainWindow window = GetWindow();
        WindowManager manager = WindowManager.Get(window);
        if (manager.WindowState == WindowState.Minimized)
        {
            manager.WindowState = WindowState.Normal;
        }

        window.AppWindow.IsShownInSwitchers = true;
        window.AppWindow.Show();
        window.Activate();
        _ = window.SetForegroundWindow();
    }

    public void Dispose()
    {
        if (_trayIcon is null)
        {
            return;
        }

        _trayIcon.LeftDoubleClick -= OnOpenRequested;
        _trayIcon.ContextMenu -= OnContextMenu;
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    private MainWindow GetWindow() => _getWindow()
        ?? throw new InvalidOperationException(
            "MainWindowがまだ作成されていません。");

    private void OnOpenRequested(TrayIcon sender, TrayIconEventArgs args) =>
        OpenRequested?.Invoke(this, EventArgs.Empty);

    private void OnContextMenu(TrayIcon sender, TrayIconEventArgs args)
    {
        bool shouldExit = false;
        MenuFlyout flyout = new();
        IReadOnlyList<TrayMenuItemDefinition> definitions =
            TrayMenuItemFactory.CreateDefinitions(GetResourceText);
        MenuFlyoutItem openItem = TrayMenuItemFactory.Create(
            definitions[0],
            () => OpenRequested?.Invoke(this, EventArgs.Empty));
        MenuFlyoutItem exitItem = TrayMenuItemFactory.Create(
            definitions[1],
            () => shouldExit = true);
        flyout.Closed += (_, _) =>
        {
            if (shouldExit)
            {
                ExitRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        flyout.Items.Add(openItem);
        flyout.Items.Add(exitItem);
        args.Flyout = flyout;
    }

    private static string GetResourceText(
        string resourceId,
        string fallback)
        => LateBoundResourceText.TryGet(resourceId, "Tray") ?? fallback;
}

internal sealed record TrayMenuItemDefinition(
    string Text,
    string AutomationId,
    string AutomationName);

internal static class TrayMenuItemFactory
{
    internal static IReadOnlyList<TrayMenuItemDefinition> CreateDefinitions(
        Func<string, string, string> resolveText)
    {
        ArgumentNullException.ThrowIfNull(resolveText);
        AppLanguage fallbackLanguage =
            LateBoundResourceText.GetEffectiveLanguage();
        return
        [
            new TrayMenuItemDefinition(
                resolveText(
                    "TrayOpenText",
                    LateBoundResourceText.GetFallback(
                        "TrayOpenText",
                        fallbackLanguage)),
                "TrayOpenMenuItem",
                resolveText(
                    "TrayOpenAutomationName",
                    LateBoundResourceText.GetFallback(
                        "TrayOpenAutomationName",
                        fallbackLanguage))),
            new TrayMenuItemDefinition(
                resolveText(
                    "TrayExitText",
                    LateBoundResourceText.GetFallback(
                        "TrayExitText",
                        fallbackLanguage)),
                "TrayExitMenuItem",
                resolveText(
                    "TrayExitAutomationName",
                    LateBoundResourceText.GetFallback(
                        "TrayExitAutomationName",
                        fallbackLanguage))),
        ];
    }

    internal static MenuFlyoutItem Create(
        TrayMenuItemDefinition definition,
        Action execute)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(execute);
        MenuFlyoutItem item = new() { Text = definition.Text };
        AutomationProperties.SetAutomationId(
            item,
            definition.AutomationId);
        AutomationProperties.SetName(item, definition.AutomationName);
        item.Click += (_, _) => execute();
        return item;
    }
}
