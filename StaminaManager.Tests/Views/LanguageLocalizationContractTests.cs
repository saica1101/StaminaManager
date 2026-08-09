using System.Xml.Linq;
using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class LanguageLocalizationContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly string[] FixedDisplayProperties =
    [
        "Text",
        "Content",
        "Header",
        "Description",
        "Title",
        "PlaceholderText",
        "ToolTip",
        "OffContent",
        "OnContent",
        "AutomationProperties.Name",
        "AutomationProperties.HelpText",
    ];

    private static readonly string[] DisplayElementNames =
    [
        "Button",
        "ComboBoxItem",
        "ContentDialog",
        "HyperlinkButton",
        "InfoBar",
        "Run",
        "TextBlock",
        "ToggleSwitch",
    ];

    private static readonly string[] FixedLiteralAllowlist = ["/"];

    private static readonly (string Uid, string[] Properties)[]
        RequiredXamlResourceProperties =
    [
        ("SettingsLanguageHeading", ["Text"]),
        ("LanguageSelector", [
            "Header",
            "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
            "Description",
            "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText",
        ]),
        ("LanguageJapaneseItem", ["Content"]),
        ("LanguageEnglishItem", ["Content"]),
        ("CompactGameSelector", ["Header"]),
        ("CompactEmptyTitle", ["Text"]),
        ("CompactEmptyDescription", ["Text"]),
        ("CompactAddGameButton", [
            "Content",
            "[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
        ]),
        ("GameNameInput", ["Header"]),
        ("CurrentStaminaInput", ["Header"]),
        ("MaxStaminaInput", ["Header"]),
        ("RecoveryIntervalHeading", ["Text"]),
        ("RecoveryMinutesInput", ["Header"]),
        ("RecoverySecondsInput", ["Header"]),
        ("GameNotificationToggle", ["Header", "OffContent", "OnContent"]),
        ("GameNotificationDescription", ["Text"]),
        ("GameImageHeading", ["Text"]),
        ("ChooseGameImageButtonText", ["Text"]),
        ("SelectedImageText", ["Text"]),
        ("GameEditorDeleteConfirmationTitle", ["Text"]),
        ("GameEditorDeleteConfirmationDescription", ["Text"]),
        ("GameEditorSaveButtonAutomationName", ["Value"]),
        ("DeleteConfirmButtonAutomationName", ["Value"]),
        ("GameEditorCancelButtonAutomationName", ["Value"]),
        ("DeleteBackButtonAutomationName", ["Value"]),
        ("RestoreDialogPrimaryButtonAutomationName", ["Value"]),
        ("RestoreDialogCancelButtonAutomationName", ["Value"]),
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
    public void LocalizationResources_ContainRequiredPropertiesInBothLocales()
    {
        foreach (string language in new[] { "ja-JP", "en-US" })
        {
            HashSet<string> keys = LoadResourceKeys(language);
            string[] missing = RequiredXamlResourceProperties
                .SelectMany(item => item.Properties.Select(property =>
                    $"{item.Uid}.{property}"))
                .Where(key => !keys.Contains(key))
                .ToArray();

            Assert.IsEmpty(
                missing,
                $"{language}に必須リソースキーがありません: "
                + string.Join(", ", missing));
        }
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
              <Style TargetType="Button">
                <Setter Property="Content">
                  <Setter.Value>Setter literal</Setter.Value>
                </Setter>
              </Style>
            </StackPanel>
            """);

        XamlLiteralViolation[] violations = FindFixedDisplayLiterals(fixture);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "Button.Content=Delete",
                "Button.AutomationProperties.HelpText=Explain delete",
                "TextBlock.Body=Body literal",
                "Setter.Value=Setter literal",
            },
            violations.Select(violation => violation.ToString()).ToArray());
    }

    [TestMethod]
    public void XamlLocalizationScanner_FixtureRequiresEveryMappedProperty()
    {
        XDocument fixture = XDocument.Parse(
            """
            <Grid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
              <ComboBox x:Uid="LanguageSelector" />
            </Grid>
            """);
        HashSet<string> resourceKeys =
        [
            "LanguageSelector.Header",
            "LanguageSelector.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
            "LanguageSelector.Description",
        ];

        string[] missing = FindMissingRequiredProperties(fixture, resourceKeys);

        CollectionAssert.AreEquivalent(
            new[]
            {
                "LanguageSelector.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText",
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
        HashSet<string> uids = documents
            .SelectMany(document => document.Descendants())
            .Select(element => AttributeValue(element, "Uid"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        string[] missingFromProduction = RequiredXamlResourceProperties
            .Select(item => item.Uid)
            .Where(uid => !uids.Contains(uid))
            .ToArray();
        Assert.IsEmpty(
            missingFromProduction,
            "必須property表のx:Uidがproduction XAMLにありません: "
            + string.Join(", ", missingFromProduction));

        foreach (string language in new[] { "ja-JP", "en-US" })
        {
            HashSet<string> keys = LoadResourceKeys(language);
            string[] missingAnyProperty = uids
                .Where(uid => !keys.Any(key => key.StartsWith(
                    uid + ".",
                    StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            Assert.IsEmpty(
                missingAnyProperty,
                $"{language}にx:Uidのproperty keyがありません: "
                + string.Join(", ", missingAnyProperty));

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
            XamlLiteralViolation[] violations = FindFixedDisplayLiterals(
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

    private static XamlLiteralViolation[] FindFixedDisplayLiterals(
        XDocument document)
    {
        List<XamlLiteralViolation> violations = [];

        foreach (XElement element in document.Descendants())
        {
            foreach (XAttribute attribute in element.Attributes()
                .Where(attribute => FixedDisplayProperties.Contains(
                    attribute.Name.LocalName,
                    StringComparer.Ordinal)))
            {
                if (IsFixedDisplayLiteral(attribute.Value))
                {
                    violations.Add(new(
                        element.Name.LocalName,
                        attribute.Name.LocalName,
                        attribute.Value.Trim()));
                }
            }

            if (DisplayElementNames.Contains(
                element.Name.LocalName,
                StringComparer.Ordinal))
            {
                foreach (string text in element.Nodes()
                    .OfType<XText>()
                    .Select(node => node.Value.Trim())
                    .Where(IsFixedDisplayLiteral))
                {
                    violations.Add(new(
                        element.Name.LocalName,
                        "Body",
                        text));
                }
            }

            if (element.Name.LocalName != "Setter"
                || !FixedDisplayProperties.Contains(
                    AttributeValue(element, "Property") ?? string.Empty,
                    StringComparer.Ordinal))
            {
                continue;
            }

            string? value = AttributeValue(element, "Value");
            if (value is not null && IsFixedDisplayLiteral(value))
            {
                violations.Add(new("Setter", "Value", value.Trim()));
            }

            foreach (XElement setterValue in element.Elements()
                .Where(child => child.Name.LocalName is "Value" or "Setter.Value"))
            {
                foreach (string text in setterValue.DescendantNodes()
                    .OfType<XText>()
                    .Select(node => node.Value.Trim())
                    .Where(IsFixedDisplayLiteral))
                {
                    violations.Add(new("Setter", "Value", text));
                }
            }
        }

        return violations.ToArray();
    }

    private static string[] FindMissingRequiredProperties(
        XDocument document,
        HashSet<string> resourceKeys)
    {
        HashSet<string> uids = document.Descendants()
            .Select(element => AttributeValue(element, "Uid"))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return RequiredXamlResourceProperties
            .Where(item => uids.Contains(item.Uid))
            .SelectMany(item => item.Properties.Select(property =>
                $"{item.Uid}.{property}"))
            .Where(key => !resourceKeys.Contains(key))
            .ToArray();
    }

    private static bool IsFixedDisplayLiteral(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && !value.TrimStart().StartsWith(
            "{",
            StringComparison.Ordinal)
        && !FixedLiteralAllowlist.Contains(
            value.Trim(),
            StringComparer.Ordinal);

    private sealed record XamlLiteralViolation(
        string Element,
        string Property,
        string Value)
    {
        public override string ToString() =>
            $"{Element}.{Property}={Value}";
    }

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
