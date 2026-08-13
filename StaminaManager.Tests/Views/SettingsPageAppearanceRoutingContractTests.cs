using System.Xml.Linq;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class SettingsPageAppearanceRoutingContractTests
{
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
    public void AcrylicOpacitySlider_標準範囲と読み上げ文言を持つ()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Views",
            "SettingsPage.xaml"));
        string resources = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Resources",
            "Strings",
            "ja-JP",
            "Resources.resw"));

        foreach (string fragment in new[]
        {
            "x:Name=\"AcrylicOpacitySlider\"",
            "x:Uid=\"AcrylicOpacitySlider\"",
            "AutomationProperties.AutomationId=\"AcrylicOpacitySlider\"",
            "AutomationProperties.HelpText=\"{x:Bind ViewModel.AcrylicOpacityHelpText, Mode=OneWay}\"",
            "IsEnabled=\"{x:Bind ViewModel.IsAcrylicOpacityEnabled, Mode=OneWay}\"",
            "Minimum=\"0\"",
            "Maximum=\"100\"",
            "StepFrequency=\"1\"",
            "SmallChange=\"1\"",
            "LargeChange=\"10\"",
            "TickFrequency=\"10\"",
            "IsThumbToolTipEnabled=\"True\"",
            "x:Name=\"AcrylicOpacityValue\"",
            "Text=\"{x:Bind ViewModel.AcrylicOpacityValueText, Mode=OneWay}\"",
        })
        {
            StringAssert.Contains(xaml, fragment);
        }

        foreach (string fragment in new[]
        {
            "AcrylicOpacitySlider.Header",
            "AcrylicOpacitySlider.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
            "AcrylicOpacitySlider.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText",
            "AcrylicOpacityValue.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
        })
        {
            StringAssert.Contains(resources, fragment);
        }
    }

    [TestMethod]
    public void ProductionXaml_SliderDoesNotDeclareUnsupportedDescription()
    {
        string root = FindRepositoryRoot();
        string[] sliderUids = Directory.EnumerateFiles(
                Path.Combine(root, "StaminaManager"),
                "*.xaml",
                SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase)
                && !path.Contains(
                    Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => XDocument.Load(path).Descendants())
            .Where(element => element.Name.LocalName == "Slider")
            .Select(element => element.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "Uid")
                ?.Value)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.IsNotEmpty(sliderUids);

        foreach (string language in new[] { "ja-JP", "en-US" })
        {
            string resourcePath = Path.Combine(
                root,
                "StaminaManager",
                "Resources",
                "Strings",
                language,
                "Resources.resw");
            HashSet<string> keys = XDocument.Load(resourcePath)
                .Root!
                .Elements("data")
                .Select(element => (string?)element.Attribute("name"))
                .OfType<string>()
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] unsupported = sliderUids
                .Where(uid => keys.Contains(
                    $"{uid}.Description",
                    StringComparer.OrdinalIgnoreCase))
                .ToArray();

            Assert.IsEmpty(
                unsupported,
                $"{language}にSlider.Descriptionリソースがあります: "
                + string.Join(", ", unsupported));
        }
    }

    [TestMethod]
    public void AcrylicOpacitySlider_購読元はLoaded側の一箇所だけ()
    {
        string root = FindRepositoryRoot();
        string xaml = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Views",
            "SettingsPage.xaml"));
        string source = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Views",
            "SettingsPage.xaml.cs"));

        Assert.AreEqual(
            0,
            xaml.Split(
                "ValueChanged=\"AcrylicOpacitySlider_ValueChanged\"",
                StringSplitOptions.None).Length - 1);
        Assert.AreEqual(
            1,
            source.Split(
                "AcrylicOpacitySlider.ValueChanged +=",
                StringSplitOptions.None).Length - 1);
        StringAssert.Contains(source, "Loaded += SettingsPage_Loaded;");
    }

    [TestMethod]
    public void AcrylicOpacitySlider_遅延commitと外観変更の直列化配線を持つ()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "Views",
            "SettingsPage.xaml.cs"));

        foreach (string fragment in new[]
        {
            "AcrylicOpacitySlider_ValueChanged",
            "DispatcherQueueTimer",
            "250",
            "PreviewAcrylicTintOpacityAsync",
            "CommitAcrylicTintOpacityAsync",
            "await FlushPendingAppearanceChangesAsync();",
            "bool committed = await _appearanceChangeRouter",
            "&& committed",
        })
        {
            StringAssert.Contains(source, fragment);
        }

        int flushIndex = source.IndexOf(
            "await FlushPendingAppearanceChangesAsync();",
            StringComparison.Ordinal);
        int backdropIndex = source.IndexOf(
            "await _appearanceChangeRouter.ChangeBackdropAsync(",
            StringComparison.Ordinal);
        Assert.IsTrue(
            flushIndex >= 0 && backdropIndex > flushIndex,
            "背景変更前に保留commitをflushする必要があります。");
    }

    [TestMethod]
    public void AcrylicOpacitySlider_ページ移動と終了時に保留commitをflushする()
    {
        string root = FindRepositoryRoot();
        string pageSource = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Views",
            "SettingsPage.xaml.cs"));
        string mainPageSource = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "MainPage.xaml.cs"));
        string appSource = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "App.xaml.cs"));

        StringAssert.Contains(
            pageSource,
            "internal async Task FlushPendingAppearanceChangesAsync()");
        Assert.DoesNotContain("Unloaded +=", pageSource);
        StringAssert.Contains(
            mainPageSource,
            "_settingsPage.FlushPendingAppearanceChangesAsync();");
        StringAssert.Contains(
            appSource,
            "await _mainPage.FlushPendingSettingsChangesAsync();");
    }

    [TestMethod]
    public void UiScript_AcrylicOpacityの診断disabled永続化と遷移を検証する()
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
            "AcrylicOpacitySlider",
            "Wait-ControlEnabled AcrylicOpacitySlider $false",
            "Wait-BackdropDiagnostic 'Solid|SolidSurface=Visible'",
            "Invoke-WinApp ui invoke NavOverview",
            "Wait-PersistedAcrylicOpacity 100",
        })
        {
            StringAssert.Contains(script, fragment);
        }

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

        Assert.IsTrue(
            diagnosticIndex >= 0
                && finalGuardIndex > diagnosticIndex
                && navigationIndex > finalGuardIndex
                && finalPersistenceIndex > navigationIndex,
            "最終100%の永続化待機はSettings離脱後に行う必要があります。");
    }

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
