using Microsoft.UI;
using Microsoft.UI.Xaml;
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
    private const double DefaultDpi = 96d;

    public MainWindow(MainPage mainPage)
    {
        ArgumentNullException.ThrowIfNull(mainPage);
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        RootFrame.Content = mainPage;
        ResizeForCurrentDpi();
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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);
}
