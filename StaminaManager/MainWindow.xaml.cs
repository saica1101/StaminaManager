using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.Windows.ApplicationModel.Resources;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;
using System.Runtime.InteropServices;
using Windows.Graphics;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace StaminaManager;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int InitialWidthEpx = 1120;
    private const int InitialHeightEpx = 760;
    private const int StandardMinimumWidthEpx = 520;
    private const int StandardMinimumHeightEpx = 520;
    private const int CompactWidthEpx = 420;
    private const int CompactHeightEpx = 520;
    private const int CompactMinimumWidthEpx = 360;
    private const int CompactMinimumHeightEpx = 480;
    private const uint DefaultDpi = 96;
    private const string AppTitleResourceId = "AppTitle";
    private const string FallbackAppTitle = "Stamina Manager";
    private WindowBounds _restoredBounds;
    private bool _shouldMaximizeOnReturn;
    private bool _isTransitioningDisplayMode;
    private bool _isApplyingMinimumSize;
    private uint _lastDpi;
    private AppDisplayMode _displayMode = AppDisplayMode.Standard;

    public MainWindow(MainPage mainPage)
    {
        ArgumentNullException.ThrowIfNull(mainPage);
        InitializeComponent();

        string appTitle = ResolveAppTitle();
        Title = appTitle;
        AppTitleBar.Title = appTitle;
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        RootFrame.Content = mainPage;
        _lastDpi = GetCurrentDpi();
        ResizeEffective(InitialWidthEpx, InitialHeightEpx);
        _restoredBounds = GetCurrentBounds();
        SetMinimumSize(
            StandardMinimumWidthEpx,
            StandardMinimumHeightEpx);
        AppWindow.Changed += OnAppWindowChanged;
        mainPage.DisplayModeChanged += SetDisplayMode;
    }

    internal void SetBackdrop(
        SystemBackdrop? systemBackdrop,
        bool isSolidSurface)
    {
        SystemBackdrop = systemBackdrop;
        SolidBackdropSurface.Visibility = isSolidSurface
            ? Visibility.Visible
            : Visibility.Collapsed;
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

    private void SetDisplayMode(AppDisplayMode displayMode)
    {
        if (_displayMode == displayMode)
        {
            return;
        }

        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        _isTransitioningDisplayMode = true;
        try
        {
            if (displayMode == AppDisplayMode.Compact)
            {
                EnterCompactMode(presenter);
            }
            else
            {
                ReturnToStandardMode(presenter);
            }
        }
        finally
        {
            _isTransitioningDisplayMode = false;
        }
    }

    private void EnterCompactMode(OverlappedPresenter presenter)
    {
        WindowDisplayModeSnapshot snapshot =
            WindowDisplayModePolicy.CaptureSnapshot(
                ToWindowPresenterState(presenter.State),
                GetCurrentBounds(),
                _restoredBounds);
        _restoredBounds = snapshot.RestoredBounds;
        _shouldMaximizeOnReturn = snapshot.ShouldMaximizeOnReturn;
        _displayMode = AppDisplayMode.Compact;

        if (presenter.State != OverlappedPresenterState.Restored)
        {
            presenter.Restore();
        }

        SetMinimumSize(
            CompactMinimumWidthEpx,
            CompactMinimumHeightEpx);
        ResizeEffective(CompactWidthEpx, CompactHeightEpx);
    }

    private void ReturnToStandardMode(OverlappedPresenter presenter)
    {
        SetMinimumSize(
            StandardMinimumWidthEpx,
            StandardMinimumHeightEpx);
        if (presenter.State != OverlappedPresenterState.Restored)
        {
            presenter.Restore();
        }

        AppWindow.MoveAndResize(ToRect(_restoredBounds));
        if (_shouldMaximizeOnReturn)
        {
            presenter.Maximize();
        }

        _displayMode = AppDisplayMode.Standard;
    }

    private void OnAppWindowChanged(
        AppWindow sender,
        AppWindowChangedEventArgs args)
    {
        uint currentDpi = GetCurrentDpi();
        if (currentDpi != _lastDpi)
        {
            _lastDpi = currentDpi;
            ApplyMinimumSizeForCurrentMode();
        }

        if ((!args.DidPositionChange && !args.DidSizeChange)
            || sender.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        WindowPresenterState presenterState =
            ToWindowPresenterState(presenter.State);
        if (WindowDisplayModePolicy.ShouldCaptureRestoredBounds(
            _displayMode,
            _isTransitioningDisplayMode,
            presenterState))
        {
            _restoredBounds = GetCurrentBounds();
        }
    }

    private void ApplyMinimumSizeForCurrentMode()
    {
        if (_isApplyingMinimumSize)
        {
            return;
        }

        if (_displayMode == AppDisplayMode.Compact)
        {
            SetMinimumSize(
                CompactMinimumWidthEpx,
                CompactMinimumHeightEpx);
            return;
        }

        SetMinimumSize(
            StandardMinimumWidthEpx,
            StandardMinimumHeightEpx);
    }

    private void SetMinimumSize(int widthEpx, int heightEpx)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        _isApplyingMinimumSize = true;
        try
        {
            presenter.PreferredMinimumWidth = ScaleEffective(widthEpx);
            presenter.PreferredMinimumHeight = ScaleEffective(heightEpx);
        }
        finally
        {
            _isApplyingMinimumSize = false;
        }
    }

    private void ResizeEffective(int widthEpx, int heightEpx) =>
        AppWindow.Resize(new SizeInt32(
            ScaleEffective(widthEpx),
            ScaleEffective(heightEpx)));

    private int ScaleEffective(int value)
    {
        return WindowDisplayModePolicy.ScaleEffectiveToPhysical(
            value,
            GetCurrentDpi());
    }

    private uint GetCurrentDpi()
    {
        nint windowHandle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        uint dpi = GetDpiForWindow(windowHandle);
        return dpi == 0 ? DefaultDpi : dpi;
    }

    private WindowBounds GetCurrentBounds() => new(
        AppWindow.Position.X,
        AppWindow.Position.Y,
        AppWindow.Size.Width,
        AppWindow.Size.Height);

    private static RectInt32 ToRect(WindowBounds bounds) => new(
        bounds.X,
        bounds.Y,
        bounds.Width,
        bounds.Height);

    private static WindowPresenterState ToWindowPresenterState(
        OverlappedPresenterState state) => state switch
    {
        OverlappedPresenterState.Maximized =>
            WindowPresenterState.Maximized,
        OverlappedPresenterState.Minimized =>
            WindowPresenterState.Minimized,
        _ => WindowPresenterState.Restored,
    };

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);
}
