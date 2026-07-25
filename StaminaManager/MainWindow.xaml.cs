using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.Windows.ApplicationModel.Resources;
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
    private const string AppTitleResourceId = "AppTitle";
    private const string FallbackAppTitle = "Stamina Manager";

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

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint windowHandle);
}
