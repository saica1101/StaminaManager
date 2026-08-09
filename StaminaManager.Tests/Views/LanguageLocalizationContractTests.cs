using System.Xml.Linq;
using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class LanguageLocalizationContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private const string AutomationNameResourceProperty =
        "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name";

    private const string AutomationHelpTextResourceProperty =
        "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText";

    private static readonly string[] DisplayProperties =
    [
        "Text",
        "Content",
        "Header",
        "Description",
        "Title",
        "PlaceholderText",
        "Message",
        "PaneTitle",
        "PrimaryButtonText",
        "SecondaryButtonText",
        "CloseButtonText",
        "ToolTipService.ToolTip",
        "OffContent",
        "OnContent",
        "AutomationProperties.Name",
        "AutomationProperties.HelpText",
    ];

    private static readonly string[] NonDisplayElementNames =
    [
        "Double",
        "Thickness",
        "CornerRadius",
        "FontFamily",
    ];

    private static readonly string[] FixedLiteralAllowlist = ["/"];

    private static readonly (string Uid, string[] Properties)[]
        RequiredPropertyOverrides =
    [
        ("LanguageSelector", [
            "Header",
            AutomationNameResourceProperty,
            "Description",
            AutomationHelpTextResourceProperty,
        ]),
        ("CompactAddGameButton", [
            "Content",
            AutomationNameResourceProperty,
        ]),
        ("GameNotificationToggle", ["Header", "OffContent", "OnContent"]),
    ];

    [TestMethod]
    public void SettingsLanguageSelector_UsesPolicyMappingAndRequiredOrder()
    {
        string root = FindRepositoryRoot();
        string xamlPath = Path.Combine(
            root,
            "StaminaManager",
            "Views",
            "SettingsPage.xaml");
        XDocument page = XDocument.Load(xamlPath);
        XElement selector = page.Descendants(Presentation + "ComboBox")
            .Single(element => AttributeValue(element, "Name")
                == "LanguageSelector");
        XElement[] items = selector
            .Elements(Presentation + "ComboBoxItem")
            .ToArray();

        Assert.AreEqual("LanguageSelector", AttributeValue(selector, "Uid"));
        StringAssert.Contains(
            AttributeValue(selector, "SelectedIndex") ?? string.Empty,
            "ViewModel.SelectedLanguageIndex");
        Assert.IsNull(AttributeValue(selector, "SelectionChanged"));
        CollectionAssert.AreEqual(
            new[] { "LanguageJapaneseItem", "LanguageEnglishItem" },
            items.Select(element => AttributeValue(element, "Uid"))
                .ToArray());

        string pageText = File.ReadAllText(xamlPath);
        Assert.IsLessThan(
            pageText.IndexOf(
                "SettingsLanguageHeading",
                StringComparison.Ordinal),
            pageText.IndexOf(
                "SettingsAppearanceHeading",
                StringComparison.Ordinal));
        Assert.IsLessThan(
            pageText.IndexOf(
                "SettingsGeneralHeading",
                StringComparison.Ordinal),
            pageText.IndexOf(
                "SettingsLanguageHeading",
                StringComparison.Ordinal));

        string source = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Views",
            "SettingsPage.xaml.cs"));
        StringAssert.Contains(source, "LanguagePolicy.TryFromSelectionIndex");
        StringAssert.Contains(source, "ViewModel.SetLanguageAsync");
        Assert.AreEqual(
            1,
            source.Split(
                "LanguageSelector.SelectionChanged +=",
                StringSplitOptions.None).Length - 1);

        Assert.AreEqual(
            0,
            LanguagePolicy.ToSelectionIndex(AppLanguage.Japanese));
        Assert.AreEqual(
            1,
            LanguagePolicy.ToSelectionIndex(AppLanguage.English));
    }

    [TestMethod]
    public void LanguageSelectorResources_UseRestartGuidanceAndSelfNames()
    {
        foreach ((string language, string expectedDescription) in new[]
        {
            ("ja-JP", "変更はアプリの次回起動時に反映されます。"),
            ("en-US", "Changes take effect the next time you start the app."),
        })
        {
            Dictionary<string, string> values = LoadResourceValues(language);

            Assert.AreEqual(
                expectedDescription,
                values.GetValueOrDefault("LanguageSelector.Description"));
            Assert.AreEqual(
                expectedDescription,
                values.GetValueOrDefault(
                    "LanguageSelector.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText"));
            Assert.AreEqual(
                "日本語",
                values.GetValueOrDefault("LanguageJapaneseItem.Content"));
            Assert.AreEqual(
                "English",
                values.GetValueOrDefault("LanguageEnglishItem.Content"));
        }
    }

    [TestMethod]
    public void XamlLocalizationScanner_FixturesRejectFixedDisplayLiterals()
    {
        XDocument fixture = XDocument.Parse(
            """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <Button Content="Delete" />
              <Button AutomationProperties.HelpText="Explain delete" />
              <TextBlock>Body literal</TextBlock>
              <TextBlock>
                <TextBlock.Text>Property element literal</TextBlock.Text>
              </TextBlock>
              <NavigationViewItem>Navigation item literal</NavigationViewItem>
              <InfoBar Message="InfoBar message" />
              <Button ToolTipService.ToolTip="Tooltip literal" />
              <ContentDialog
                PrimaryButtonText="Primary button"
                SecondaryButtonText="Secondary button"
                CloseButtonText="Close button" />
              <Style TargetType="Button">
                <Setter Property="Content" Value="Setter attribute literal" />
                <Setter Property="Content">
                  <Setter.Value>Setter literal</Setter.Value>
                </Setter>
              </Style>
            </StackPanel>
            """);

        string[] violations = FindFixedDisplayLiterals(fixture);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "Button.Content=Delete",
                "Button.AutomationProperties.HelpText=Explain delete",
                "TextBlock.Text=Body literal",
                "TextBlock.Text=Property element literal",
                "NavigationViewItem.Text=Navigation item literal",
                "InfoBar.Message=InfoBar message",
                "Button.ToolTipService.ToolTip=Tooltip literal",
                "ContentDialog.PrimaryButtonText=Primary button",
                "ContentDialog.SecondaryButtonText=Secondary button",
                "ContentDialog.CloseButtonText=Close button",
                "Setter.Value=Setter attribute literal",
                "Setter.Value=Setter literal",
            },
            violations);
    }

    [TestMethod]
    public void XamlLocalizationScanner_FixtureRequiresEveryMappedProperty()
    {
        XDocument fixture = XDocument.Parse(
            """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ComboBox x:Uid="LanguageSelector" />
              <TextBlock x:Uid="SettingsPageTitle" />
            </Grid>
            """);
        HashSet<string> resourceKeys =
        [
            "LanguageSelector.Header",
            "LanguageSelector.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
            "LanguageSelector.Description",
            "SettingsPageTitle.Tag",
        ];

        string[] missing = FindMissingRequiredProperties(fixture, resourceKeys);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "LanguageSelector.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText",
                "SettingsPageTitle.Text",
            },
            missing);
    }

    [TestMethod]
    public void ProductionXaml_UidHasRequiredPropertiesInBothLocales()
    {
        string root = FindRepositoryRoot();
        XDocument[] documents = GetProductionXamlPaths(root)
            .Select(XDocument.Load)
            .ToArray();

        foreach (string language in new[] { "ja-JP", "en-US" })
        {
            HashSet<string> keys = LoadResourceKeys(language);
            string[] missing = documents
                .SelectMany(document =>
                    FindMissingRequiredProperties(document, keys))
                .ToArray();

            Assert.IsEmpty(
                missing,
                $"{language}にx:Uidの必須property keyがありません: "
                + string.Join(", ", missing));
        }
    }

    [TestMethod]
    public void ProductionXaml_DoesNotHardCodeDisplayLiterals()
    {
        string root = FindRepositoryRoot();
        foreach (string path in GetProductionXamlPaths(root))
        {
            string[] violations = FindFixedDisplayLiterals(
                XDocument.Load(path));

            Assert.IsEmpty(
                violations,
                $"{Path.GetRelativePath(root, path)}に固定表示文言があります: "
                + string.Join(", ", violations));
        }
    }

    private static HashSet<string> LoadResourceKeys(string language)
        => LoadResourceValues(language).Keys
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, string> LoadResourceValues(string language)
    {
        XDocument document = XDocument.Load(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "Resources",
            "Strings",
            language,
            "Resources.resw"));
        return document.Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => (string?)element.Element("value") ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static string[] FindFixedDisplayLiterals(
        XDocument document)
    {
        List<string> violations = [];

        foreach (XElement element in document.Descendants())
        {
            foreach (XAttribute attribute in element.Attributes()
                .Where(attribute => DisplayProperties.Contains(
                    attribute.Name.LocalName,
                    StringComparer.Ordinal)))
            {
                if (IsFixedDisplayLiteral(attribute.Value))
                {
                    violations.Add(
                        $"{element.Name.LocalName}.{attribute.Name.LocalName}="
                        + attribute.Value.Trim());
                }
            }

            if (element.Name.LocalName == "Setter"
                && DisplayProperties.Contains(
                    (string?)element.Attribute("Property") ?? string.Empty,
                    StringComparer.Ordinal)
                && element.Attribute("Value") is { } value
                && IsFixedDisplayLiteral(value.Value))
            {
                violations.Add($"Setter.Value={value.Value.Trim()}");
            }

            if (NonDisplayElementNames.Contains(
                element.Name.LocalName,
                StringComparer.Ordinal))
            {
                continue;
            }

            foreach (string text in element.Nodes()
                .OfType<XText>()
                .Select(node => node.Value.Trim())
                .Where(IsFixedDisplayLiteral))
            {
                string target = element.Name.LocalName.Contains(
                    '.',
                    StringComparison.Ordinal)
                    ? element.Name.LocalName
                    : $"{element.Name.LocalName}.Text";
                violations.Add($"{target}={text}");
            }
        }

        return violations.ToArray();
    }

    private static string[] FindMissingRequiredProperties(
        XDocument document,
        HashSet<string> resourceKeys)
    {
        List<string> missing = [];

        foreach (XElement element in document.Descendants())
        {
            string? uid = AttributeValue(element, "Uid");
            if (uid is null)
            {
                continue;
            }

            foreach (string property in GetRequiredProperties(element, uid))
            {
                string key = $"{uid}.{property}";
                if (!resourceKeys.Contains(key))
                {
                    missing.Add(key);
                }
            }
        }

        return missing.ToArray();
    }

    private static string[] GetRequiredProperties(XElement element, string uid)
    {
        string primaryProperty = GetPrimaryProperty(element, uid);
        foreach ((string overrideUid, string[] properties)
            in RequiredPropertyOverrides)
        {
            if (string.Equals(overrideUid, uid, StringComparison.Ordinal))
            {
                return properties;
            }
        }

        return [primaryProperty];
    }

    private static string GetPrimaryProperty(XElement element, string uid) =>
        element.Name.LocalName switch
        {
            "TextBlock" => HasDynamicText(element)
                ? AutomationNameResourceProperty
                : "Text",
            "ComboBoxItem" or "NavigationViewItem" => "Content",
            "InfoBar" => "Title",
            "NavigationView" => "PaneTitle",
            "ComboBox" or "NumberBox" or "TextBox" or "ToggleSwitch"
                or "Slider" => "Header",
            "Setter" => "Value",
            "Button" => element.Elements().Any()
                ? AutomationNameResourceProperty
                : "Content",
            "ContentControl" or "Image" or "ItemsRepeater"
                or "ProgressRing" or "StackPanel" =>
                AutomationNameResourceProperty,
            _ => throw new AssertFailedException(
                $"x:Uid付きの未知要素型です: {element.Name.LocalName} "
                + $"(x:Uid={uid})"),
        };

    private static bool HasDynamicText(XElement element) =>
        element.DescendantsAndSelf()
            .Attributes()
            .Any(attribute => attribute.Name.LocalName == "Text"
                && IsMarkupExtension(attribute.Value))
        || element.Elements()
            .Where(child => child.Name.LocalName == "TextBlock.Text")
            .SelectMany(child => child.Nodes().OfType<XText>())
            .Any(node => IsMarkupExtension(node.Value));

    private static bool IsFixedDisplayLiteral(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !IsMarkupExtension(value)
        && !FixedLiteralAllowlist.Contains(
            value.Trim(),
            StringComparer.Ordinal);

    private static bool IsMarkupExtension(string value) =>
        value.TrimStart().StartsWith("{", StringComparison.Ordinal);

    private static IEnumerable<string> GetProductionXamlPaths(string root) =>
        Directory.EnumerateFiles(
            Path.Combine(root, "StaminaManager"),
            "*.xaml",
            SearchOption.AllDirectories)
        .Where(path => !path.Contains(
            Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase)
            && !path.Contains(
                Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase));

    private static string? AttributeValue(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute =>
            attribute.Name.LocalName == name)?.Value;

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
