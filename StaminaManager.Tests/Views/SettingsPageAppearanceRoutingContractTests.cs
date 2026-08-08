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

    [TestMethod]
    public void AcrylicOpacitySlider_UsesStandardRangeAndDeferredCommit()
    {
        string xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "Views",
            "SettingsPage.xaml"));
        string source = LoadSettingsPageSource();

        StringAssert.Contains(xaml, "x:Name=\"AcrylicOpacitySlider\"");
        StringAssert.Contains(
            xaml,
            "AutomationProperties.AutomationId=\"AcrylicOpacitySlider\"");
        foreach (string property in new[]
        {
            "Minimum=\"0\"",
            "Maximum=\"100\"",
            "StepFrequency=\"1\"",
            "SmallChange=\"1\"",
            "LargeChange=\"10\"",
            "TickFrequency=\"10\"",
            "IsThumbToolTipEnabled=\"True\"",
        })
        {
            StringAssert.Contains(xaml, property);
        }

        StringAssert.Contains(xaml, "AcrylicOpacitySlider_ValueChanged");
        StringAssert.Contains(source, "DispatcherQueueTimer");
        StringAssert.Contains(source, "250");
        StringAssert.Contains(source, "PreviewAcrylicTintOpacityAsync");
        StringAssert.Contains(source, "CommitAcrylicTintOpacityAsync");
        StringAssert.Contains(source, "FlushPendingAppearanceChangesAsync");
    }

    [TestMethod]
    public void PendingAcrylicOpacity_IsFlushedAfterNavigationAndBeforeShutdown()
    {
        string pageSource = LoadSettingsPageSource();
        StringAssert.Contains(pageSource, "_pendingAcrylicOpacityPercent");
        StringAssert.Contains(pageSource, "_acrylicOpacityChangeVersion");
        StringAssert.Contains(pageSource, "QueueAcrylicOpacityCommit();");
        StringAssert.Contains(pageSource, "await _acrylicOpacityOperation;");
        Assert.DoesNotContain("Unloaded +=", pageSource);

        string mainPageSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "MainPage.xaml.cs"));
        StringAssert.Contains(
            mainPageSource,
            "_settingsPage.FlushPendingAppearanceChangesAsync()");

        string appSource = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "App.xaml.cs"));
        StringAssert.Contains(
            appSource,
            "await _mainPage.FlushPendingSettingsChangesAsync();");
    }

    [TestMethod]
    public void AcrylicOpacityPreview_UsesLatestChangeVersion()
    {
        string source = LoadSettingsPageSource();
        int start = source.IndexOf(
            "private void QueueAcrylicOpacityPreview",
            StringComparison.Ordinal);
        int end = source.IndexOf(
            "private void QueueAcrylicOpacityCommit",
            start,
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, start);
        Assert.IsGreaterThan(start, end);
        string previewMethod = source[start..end];
        StringAssert.Contains(previewMethod, "_acrylicOpacityChangeVersion");
        StringAssert.Contains(
            previewMethod,
            "_pendingAcrylicOpacityPercent != percent");
    }

    [TestMethod]
    public void AcrylicOpacityValueChanged_同じ値へ戻す入力を同期更新と混同しない()
    {
        string handler = ExtractMethod(
            LoadSettingsPageSource(),
            "private void AcrylicOpacitySlider_ValueChanged(",
            "private async void CloseBehaviorSelector_SelectionChanged(");

        StringAssert.Contains(handler, "_isSynchronizingControls");
        StringAssert.Contains(
            handler,
            "_pendingAcrylicOpacityPercent = percent;");
        StringAssert.Contains(
            handler,
            "_acrylicOpacityChangeVersion++;");
        Assert.DoesNotContain(
            "args.NewValue == ViewModel.AcrylicTintOpacityPercent",
            handler);
    }

    [TestMethod]
    public void BackdropHandler_MicaとSolidの変更前に保留AcrylicCommitをflushする()
    {
        string handler = ExtractMethod(
            LoadSettingsPageSource(),
            "private async void BackdropSelector_SelectionChanged(",
            "private async void CloseBehaviorSelector_SelectionChanged(");
        int flushIndex = handler.IndexOf(
            "await FlushPendingAppearanceChangesAsync();",
            StringComparison.Ordinal);
        int backdropIndex = handler.IndexOf(
            "await _appearanceChangeRouter.ChangeBackdropAsync(",
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, flushIndex);
        Assert.IsGreaterThanOrEqualTo(0, backdropIndex);
        Assert.IsGreaterThan(flushIndex, backdropIndex);
    }

    [TestMethod]
    public void AcrylicOpacityCommit_拒否時に保留値を消去しない()
    {
        string source = LoadSettingsPageSource();
        string method = ExtractMethod(
            source,
            "private void QueueAcrylicOpacityCommit()",
            "private void QueueAcrylicOpacityOperation(");

        StringAssert.Contains(
            method,
            "bool committed = await _appearanceChangeRouter");
        StringAssert.Contains(method, "&& committed");
    }

    [TestMethod]
    public void AcrylicOpacityRollback_同期メソッドだけがSlider値を更新する()
    {
        string xaml = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "Views",
            "SettingsPage.xaml"));
        string source = LoadSettingsPageSource();
        string synchronizeMethod = ExtractMethod(
            source,
            "private void SynchronizeControls()",
            "internal async Task FlushPendingAppearanceChangesAsync()");

        Assert.DoesNotContain(
            "Value=\"{x:Bind ViewModel.AcrylicTintOpacityPercent, Mode=OneWay}\"",
            xaml);
        StringAssert.Contains(
            synchronizeMethod,
            "_isSynchronizingControls = true;");
        StringAssert.Contains(
            synchronizeMethod,
            "AcrylicOpacitySlider.Value =");
        StringAssert.Contains(
            synchronizeMethod,
            "_isSynchronizingControls = false;");
    }

    [TestMethod]
    public void UiScript_AcrylicOpacityExtremesDiagnosticDisabledAndPersistence()
    {
        string script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "ui",
            "StaminaManager.UiTests.ps1"));

        foreach (string fragment in new[]
        {
            "@(0, 50, 100)",
            "Acrylic|TintOpacity=0.00|SolidSurface=Collapsed",
            "Acrylic|TintOpacity=0.50|SolidSurface=Collapsed",
            "Acrylic|TintOpacity=1.00|SolidSurface=Collapsed",
            "Get-RawBackdropDiagnostic",
            "AcrylicOpacitySlider",
            "-p IsEnabled --value False",
            "acrylicTintOpacityPercent",
            "'Mica'",
            "'Solid'",
        })
        {
            StringAssert.Contains(script, fragment);
        }
    }

    [TestMethod]
    public void UiScript_UsesCompleteSolidFallbackDiagnostic()
    {
        string script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "ui",
            "StaminaManager.UiTests.ps1"));

        StringAssert.Contains(
            script,
            "Wait-BackdropDiagnostic 'Solid|SolidSurface=Visible'");
    }

    [TestMethod]
    public void UiScript_WaitsForFinal100PersistenceAfterSettingsRoundTrip()
    {
        string script = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "ui",
            "StaminaManager.UiTests.ps1"));
        int diagnosticIndex = script.IndexOf(
            "Wait-BackdropDiagnostic $expectedDiagnostics[$percent]",
            StringComparison.Ordinal);
        int finalGuardIndex = script.IndexOf(
            "if ($percent -ne 100)",
            diagnosticIndex,
            StringComparison.Ordinal);
        int navigationIndex = script.IndexOf(
            "Invoke-WinApp ui invoke NavOverview",
            diagnosticIndex,
            StringComparison.Ordinal);
        int finalPersistenceIndex = script.IndexOf(
            "Wait-PersistedAcrylicOpacity 100",
            navigationIndex,
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, diagnosticIndex);
        Assert.IsGreaterThan(diagnosticIndex, finalGuardIndex);
        Assert.IsGreaterThan(finalGuardIndex, navigationIndex);
        Assert.IsGreaterThan(navigationIndex, finalPersistenceIndex);
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
