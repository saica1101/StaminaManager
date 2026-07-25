using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
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
    private const double DefaultDpi = 96d;
    private const string AppTitleResourceId = "AppTitle";
    private const string FallbackAppTitle = "Stamina Manager";
    private PointInt32? _normalPosition;
    private SizeInt32? _normalSize;
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
        ResizeForCurrentDpi();
        SetMinimumSize(
            StandardMinimumWidthEpx,
            StandardMinimumHeightEpx);
        mainPage.DisplayModeChanged += SetDisplayMode;
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

    private void ResizeForCurrentDpi()
    {
        nint windowHandle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        uint dpi = GetDpiForWindow(windowHandle);
        double scale = dpi == 0 ? 1d : dpi / DefaultDpi;
        AppWindow.Resize(new SizeInt32(
            checked((int)Math.Round(InitialWidthEpx * scale)),
            checked((int)Math.Round(InitialHeightEpx * scale))));
    }

    private void SetDisplayMode(AppDisplayMode displayMode)
    {
        if (_displayMode == displayMode)
        {
            return;
        }

        if (displayMode == AppDisplayMode.Compact)
        {
            _normalPosition = AppWindow.Position;
            _normalSize = AppWindow.Size;
            SetMinimumSize(
                CompactMinimumWidthEpx,
                CompactMinimumHeightEpx);
            ResizeEffective(CompactWidthEpx, CompactHeightEpx);
        }
        else
        {
            SetMinimumSize(
                StandardMinimumWidthEpx,
                StandardMinimumHeightEpx);
            PointInt32 position = _normalPosition
                ?? AppWindow.Position;
            SizeInt32 size = _normalSize ?? new SizeInt32(
                ScaleEffective(InitialWidthEpx),
                ScaleEffective(InitialHeightEpx));
            AppWindow.MoveAndResize(new RectInt32(
                position.X,
                position.Y,
                size.Width,
                size.Height));
        }

        _displayMode = displayMode;
    }

    private void SetMinimumSize(int widthEpx, int heightEpx)
    {
        if (AppWindow.Presenter is not OverlappedPresenter presenter)
        {
            return;
        }

        presenter.PreferredMinimumWidth = ScaleEffective(widthEpx);
        presenter.PreferredMinimumHeight = ScaleEffective(heightEpx);
    }

    private void ResizeEffective(int widthEpx, int heightEpx) =>
        AppWindow.Resize(new SizeInt32(
            ScaleEffective(widthEpx),
            ScaleEffective(heightEpx)));

    private int ScaleEffective(int value)
    {
        nint windowHandle = Win32Interop.GetWindowFromWindowId(AppWindow.Id);
        uint dpi = GetDpiForWindow(windowHandle);
        double scale = dpi == 0 ? 1d : dpi / DefaultDpi;
        return checked((int)Math.Round(value * scale));
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);
}
