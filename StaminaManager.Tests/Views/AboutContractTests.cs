using System.Xml.Linq;
using Microsoft.UI.Xaml;
using StaminaManager;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class AboutContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [TestMethod]
    public void MainPage_AboutIsFooterItem_AndVersionIsPaneFooter()
    {
        XDocument page = XDocument.Load(GetPath("StaminaManager", "MainPage.xaml"));
        XElement navigation = page.Descendants(Presentation + "NavigationView")
            .Single();
        XElement about = navigation
            .Element(Presentation + "NavigationView.FooterMenuItems")!
            .Descendants(Presentation + "NavigationViewItem")
            .Single();
        XElement versionBand = navigation
            .Element(Presentation + "NavigationView.PaneFooter")!
            .Descendants()
            .Single(element => AttributeValue(
                element,
                "AutomationProperties.AutomationId") == "VersionFooterBand");
        XElement versionText = versionBand
            .Descendants()
            .Single(element => AttributeValue(
                element,
                "AutomationProperties.AutomationId") == "VersionFooterText");

        Assert.AreEqual("NavAbout", AttributeValue(
            about,
            "AutomationProperties.AutomationId"));
        Assert.AreEqual("About", AttributeValue(about, "Tag"));
        Assert.AreEqual("VersionFooterText", AttributeValue(
            versionText,
            "AutomationProperties.AutomationId"));
        StringAssert.Contains(
            AttributeValue(versionText, "Text") ?? string.Empty,
            "VersionText");

        string xaml = File.ReadAllText(GetPath("StaminaManager", "MainPage.xaml"));
        int paneFooterIndex = xaml.IndexOf(
            "<NavigationView.PaneFooter>",
            StringComparison.Ordinal);
        int footerMenuItemsIndex = xaml.IndexOf(
            "<NavigationView.FooterMenuItems>",
            StringComparison.Ordinal);
        Assert.IsTrue(
            paneFooterIndex >= 0
                && footerMenuItemsIndex > paneFooterIndex);

    }

    [TestMethod]
    public void MainPage_HidesVersionFooterWhenPaneIsClosed_AndPreservesCompactMode()
    {
        XDocument page = XDocument.Load(GetPath("StaminaManager", "MainPage.xaml"));
        XElement navigation = page.Descendants(Presentation + "NavigationView")
            .Single();

        StringAssert.Contains(
            AttributeValue(navigation, "PaneOpened") ?? string.Empty,
            "ShellNavigation_PaneOpened");
        StringAssert.Contains(
            AttributeValue(navigation, "PaneClosed") ?? string.Empty,
            "ShellNavigation_PaneClosed");

        Assert.AreEqual(
            Visibility.Visible,
            MainPage.GetVersionFooterVisibility(
                isPaneOpen: true,
                isCompact: false));
        Assert.AreEqual(
            Visibility.Collapsed,
            MainPage.GetVersionFooterVisibility(
                isPaneOpen: false,
                isCompact: false));
        Assert.AreEqual(
            Visibility.Collapsed,
            MainPage.GetVersionFooterVisibility(
                isPaneOpen: true,
                isCompact: true));
    }

    [TestMethod]
    public void AboutPage_UsesAccessibleNativeControls_AndNoNetworkSurface()
    {
        XDocument page = XDocument.Load(GetPath(
            "StaminaManager",
            "Views",
            "AboutPage.xaml"));
        XElement[] buttons = page.Descendants(Presentation + "Button").ToArray();

        foreach (string automationId in new[]
        {
            "OpenGitHubButton",
            "OpenReadmeButton",
        })
        {
            XElement button = buttons.Single(element => AttributeValue(
                element,
                "AutomationProperties.AutomationId") == automationId);
            Assert.IsFalse(string.IsNullOrWhiteSpace(AttributeValue(
                button,
                "Uid")));
            StringAssert.Contains(
                AttributeValue(button, "Command") ?? string.Empty,
                "ViewModel.");
        }

        Assert.IsTrue(page.Descendants(Presentation + "InfoBar").Any());
        string source = File.ReadAllText(GetPath(
            "StaminaManager",
            "Views",
            "AboutPage.xaml.cs"));
        Assert.DoesNotContain("WebView2", source);
        Assert.DoesNotContain("HttpClient", source);
    }

    [TestMethod]
    public void AboutPage_IsSafeForNarrowWindowsAndThemeContrast()
    {
        string xaml = File.ReadAllText(GetPath(
            "StaminaManager",
            "Views",
            "AboutPage.xaml"));
        string tokens = File.ReadAllText(GetPath(
            "StaminaManager",
            "Resources",
            "DesignTokens.xaml"));

        StringAssert.Contains(xaml, "HorizontalScrollBarVisibility=\"Disabled\"");
        StringAssert.Contains(xaml, "TextWrapping=\"Wrap\"");
        Assert.DoesNotContain("Background=\"#", xaml);
        Assert.DoesNotContain("Foreground=\"#", xaml);
        StringAssert.Contains(tokens, "x:Key=\"HighContrast\"");
    }

    [TestMethod]
    public void AboutPage_UsesVerticalVersionAndWrappingButtonsWithoutFixedWidth()
    {
        XDocument page = XDocument.Load(GetPath(
            "StaminaManager",
            "Views",
            "AboutPage.xaml"));
        XElement versionText = page.Descendants()
            .Single(element => AttributeValue(
                element,
                "AutomationProperties.AutomationId") == "AboutVersionText");
        XElement versionStack = versionText.Parent!;

        Assert.AreNotEqual("Horizontal", AttributeValue(versionStack, "Orientation"));

        foreach (string automationId in new[]
        {
            "OpenGitHubButton",
            "OpenReadmeButton",
        })
        {
            XElement button = page.Descendants(Presentation + "Button")
                .Single(element => AttributeValue(
                    element,
                    "AutomationProperties.AutomationId") == automationId);
            XElement text = button.Descendants(Presentation + "TextBlock")
                .Single();

            Assert.AreEqual("Stretch", AttributeValue(button, "HorizontalAlignment"));
            Assert.IsNull(AttributeValue(button, "Width"));
            Assert.IsNull(AttributeValue(button, "MinWidth"));
            Assert.AreEqual("Wrap", AttributeValue(text, "TextWrapping"));
        }
    }

    [TestMethod]
    public void AboutPage_HasDedicatedReadmeHeadingAndDescription()
    {
        XDocument page = XDocument.Load(GetPath(
            "StaminaManager",
            "Views",
            "AboutPage.xaml"));
        string xaml = File.ReadAllText(GetPath(
            "StaminaManager",
            "Views",
            "AboutPage.xaml"));

        Assert.IsTrue(page.Descendants()
            .Any(element => AttributeValue(element, "Uid") == "AboutReadmeHeading"));
        Assert.IsTrue(page.Descendants()
            .Any(element => AttributeValue(element, "Uid") == "AboutReadmeDescription"));
        StringAssert.Contains(xaml, "OpenGitHubButton");
        StringAssert.Contains(xaml, "OpenReadmeButton");

        XDocument resources = XDocument.Load(GetPath(
            "StaminaManager",
            "Resources",
            "Strings",
            "ja-JP",
            "Resources.resw"));
        Dictionary<string, string> values = resources.Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

        Assert.AreEqual("README", values["AboutReadmeHeading.Text"]);
        Assert.AreEqual(
            "機能、使い方、ビルド手順、注意事項を GitHub の README で確認できます。",
            values["AboutReadmeDescription.Text"]);
    }

    [TestMethod]
    public void AboutResources_ExplainBrowserLaunchAndAvoidLicenseConclusion()
    {
        XDocument resources = XDocument.Load(GetPath(
            "StaminaManager",
            "Resources",
            "Strings",
            "ja-JP",
            "Resources.resw"));
        Dictionary<string, string> values = resources.Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

        foreach (string button in new[]
        {
            "OpenGitHubButton",
            "OpenReadmeButton",
        })
        {
            StringAssert.Contains(
                values[$"{button}.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"],
                "既定のブラウザー");
            StringAssert.Contains(
                values[$"{button}.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText"],
                "既定のブラウザー");
        }

        Assert.IsFalse(values.Values.Any(value =>
            value.Contains("再配布可能", StringComparison.Ordinal)
            || value.Contains("ライセンスが確定", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void AboutUiChecks_AreConnectedToExistingSuiteWithoutInvokingLinks()
    {
        string suite = File.ReadAllText(GetPath(
            "tests",
            "ui",
            "StaminaManager.UiTests.ps1"));

        StringAssert.Contains(suite, "Invoke-UiTest About");
        StringAssert.Contains(suite, "NavAbout");
        Assert.DoesNotContain("ui invoke OpenGitHubButton", suite);
        Assert.DoesNotContain("ui invoke OpenReadmeButton", suite);
    }

    private static string? AttributeValue(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == name)?.Value;

    private static string GetPath(params string[] parts)
    {
        string root = FindRepositoryRoot();
        return parts.Aggregate(root, Path.Combine);
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
