using System.Xml.Linq;

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

        string source = File.ReadAllText(GetPath("StaminaManager", "MainPage.xaml.cs"));
        StringAssert.Contains(source, "AppPage.About");
        StringAssert.Contains(source, "AboutNavigationItem");
        StringAssert.Contains(source, "_aboutPage");
        StringAssert.Contains(source, "ShellNavigation.Visibility");
        StringAssert.Contains(source, "Visibility.Collapsed");
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
    public void AboutUiScript_VerifiesNavigationAndLinksWithoutInvokingBrowser()
    {
        string script = File.ReadAllText(GetPath(
            "tests",
            "ui",
            "about-navigation.ps1"));

        foreach (string requiredText in new[]
        {
            "NavAbout",
            "OpenGitHubButton",
            "OpenReadmeButton",
            "AboutPageRoot",
        })
        {
            StringAssert.Contains(script, requiredText);
        }

        Assert.DoesNotContain("invoke', 'OpenGitHubButton", script);
        Assert.DoesNotContain("invoke', 'OpenReadmeButton", script);
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
