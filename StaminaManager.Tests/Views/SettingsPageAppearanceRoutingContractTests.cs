namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class SettingsPageAppearanceRoutingContractTests
{
    [TestMethod]
    public void Constructor_InitializeComponent後に外観ルーターを構築する()
    {
        string source = LoadSettingsPageSource();
        int initializeIndex = source.IndexOf(
            "InitializeComponent();",
            StringComparison.Ordinal);
        int routerIndex = source.IndexOf(
            "_appearanceChangeRouter = new SettingsAppearanceChangeRouter(",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, initializeIndex);
        Assert.IsGreaterThan(initializeIndex, routerIndex);
        StringAssert.Contains(
            source,
            "new DispatcherQueueUiWorkQueue(DispatcherQueue)");
        StringAssert.Contains(source, "ViewModel.ReportUnexpectedFailure");
        StringAssert.Contains(source, "SynchronizeControls");
    }

    [TestMethod]
    public void ThemeHandler_イベント時の要求値だけをルーターへ渡す()
    {
        string handler = ExtractMethod(
            LoadSettingsPageSource(),
            "private async void ThemeToggle_Toggled(",
            "private async void BackdropSelector_SelectionChanged(");

        StringAssert.Contains(
            handler,
            "AppTheme requestedTheme = ThemeToggle.IsOn");
        StringAssert.Contains(
            handler,
            "await _appearanceChangeRouter.ChangeThemeAsync(requestedTheme);");
        Assert.AreEqual(
            1,
            CountOccurrences(handler, "ThemeToggle.IsOn"),
            "キュー処理中にThemeToggleを読み直してはいけません。");
        Assert.DoesNotContain("ViewModel.SetThemeAsync", handler);
        Assert.DoesNotContain("ExecuteSettingChangeAsync", handler);
    }

    [TestMethod]
    public void BackdropHandler_イベント時の要求値だけをルーターへ渡す()
    {
        string handler = ExtractMethod(
            LoadSettingsPageSource(),
            "private async void BackdropSelector_SelectionChanged(",
            "private async void CloseBehaviorSelector_SelectionChanged(");

        StringAssert.Contains(
            handler,
            "BackdropPolicy.TryFromSelectionIndex(");
        Assert.DoesNotContain(
            "(BackdropKind)BackdropSelector.SelectedIndex",
            handler);
        StringAssert.Contains(
            handler,
            "await _appearanceChangeRouter.ChangeBackdropAsync(");
        StringAssert.Contains(handler, "requestedBackdrop);");
        Assert.AreEqual(
            1,
            CountOccurrences(handler, "BackdropSelector.SelectedIndex"),
            "イベント時の選択値を1回だけ読み取る必要があります。");
        Assert.DoesNotContain("ViewModel.SetBackdropAsync", handler);
        Assert.DoesNotContain("ExecuteSettingChangeAsync", handler);
    }

    [TestMethod]
    public void BackdropSelector_BlurとTransparentを表示しない()
    {
        string xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "Views",
            "SettingsPage.xaml"));

        StringAssert.Contains(xaml, "BackdropMicaItem");
        StringAssert.Contains(xaml, "BackdropAcrylicItem");
        StringAssert.Contains(xaml, "BackdropSolidItem");
        Assert.DoesNotContain("BackdropBlurItem", xaml);
        Assert.DoesNotContain("BackdropTransparentItem", xaml);
    }

    private static string ExtractMethod(
        string source,
        string startMarker,
        string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.IsGreaterThan(start, end);
        return source[start..end];
    }

    private static int CountOccurrences(string value, string search)
    {
        int count = 0;
        int index = 0;
        while ((index = value.IndexOf(
            search,
            index,
            StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static string LoadSettingsPageSource() => File.ReadAllText(
        Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "Views",
            "SettingsPage.xaml.cs"));

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(
                directory.FullName,
                "StaminaManager.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new AssertFailedException(
            "リポジトリ ルートを検出できません。");
    }
}
