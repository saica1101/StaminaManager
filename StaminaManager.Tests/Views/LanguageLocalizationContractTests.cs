using System.Xml.Linq;
using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class LanguageLocalizationContractTests
{
    private static readonly XNamespace Presentation =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static readonly string[] RequiredResourceKeys =
    [
        "SettingsLanguageHeading.Text",
        "LanguageSelector.Header",
        "LanguageSelector.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
        "LanguageJapaneseItem.Content",
        "LanguageEnglishItem.Content",
        "CompactGameSelector.Header",
        "CompactEmptyTitle.Text",
        "CompactEmptyDescription.Text",
        "CompactAddGameButton.Content",
        "CompactAddGameButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name",
        "GameNameInput.Header",
        "CurrentStaminaInput.Header",
        "MaxStaminaInput.Header",
        "RecoveryIntervalHeading.Text",
        "RecoveryMinutesInput.Header",
        "RecoverySecondsInput.Header",
        "GameNotificationToggle.Header",
        "GameNotificationToggle.OffContent",
        "GameNotificationToggle.OnContent",
        "GameNotificationDescription.Text",
        "GameImageHeading.Text",
        "ChooseGameImageButtonText.Text",
        "SelectedImageText.Text",
        "GameEditorDeleteConfirmationTitle.Text",
        "GameEditorDeleteConfirmationDescription.Text",
        "GameEditorSaveButtonAutomationName.Value",
        "DeleteConfirmButtonAutomationName.Value",
        "GameEditorCancelButtonAutomationName.Value",
        "DeleteBackButtonAutomationName.Value",
        "RestoreDialogPrimaryButtonAutomationName.Value",
        "RestoreDialogCancelButtonAutomationName.Value",
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
            string[] missing = RequiredResourceKeys
                .Where(key => !keys.Contains(key))
                .ToArray();

            Assert.IsEmpty(
                missing,
                $"{language}に必須リソースキーがありません: "
                + string.Join(", ", missing));
        }
    }

    [TestMethod]
    public void ProductionXaml_UidHasAPropertyInBothLocales()
    {
        string root = FindRepositoryRoot();
        string[] uids = GetProductionXamlPaths(root)
            .SelectMany(path => XDocument.Load(path).Descendants())
            .Select(element => AttributeValue(element, "Uid"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        foreach (string language in new[] { "ja-JP", "en-US" })
        {
            HashSet<string> keys = LoadResourceKeys(language);
            string[] missing = uids
                .Where(uid => !keys.Any(key => key.StartsWith(
                    uid + ".",
                    StringComparison.OrdinalIgnoreCase)))
                .ToArray();

            Assert.IsEmpty(
                missing,
                $"{language}にx:Uidのproperty keyがありません: "
                + string.Join(", ", missing));
        }
    }

    [TestMethod]
    public void ProductionXaml_DoesNotHardCodeLocalizedProperties()
    {
        string root = FindRepositoryRoot();
        string[] properties =
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
        ];

        foreach (string path in GetProductionXamlPaths(root))
        {
            XDocument document = XDocument.Load(path);
            foreach (XElement element in document.Descendants())
            {
                string? uid = AttributeValue(element, "Uid");
                foreach (XAttribute attribute in element.Attributes())
                {
                    if (attribute.Name.NamespaceName ==
                        "http://schemas.microsoft.com/winfx/2006/xaml"
                        || !properties.Contains(attribute.Name.LocalName)
                        || attribute.Value.StartsWith('{')
                        || attribute.Value == "/")
                    {
                        continue;
                    }

                    Assert.Fail(
                        $"{Path.GetRelativePath(root, path)}の"
                        + $"{element.Name.LocalName}.{attribute.Name.LocalName}"
                        + $"に固定文言があります: {attribute.Value}"
                        + $" (x:Uid={uid ?? "なし"})");
                }
            }
        }

        string styles = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Resources",
            "Styles.xaml"));
        Assert.IsFalse(
            styles.Any(IsJapaneseCharacter),
            "Styles.xamlに固定日本語が残っています。");
    }

    private static HashSet<string> LoadResourceKeys(string language)
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
            .Select(element => (string)element.Attribute("name")!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
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

    private static bool IsJapaneseCharacter(char value) =>
        value is >= '\u3040' and <= '\u30ff'
            or >= '\u3400' and <= '\u4dbf'
            or >= '\u4e00' and <= '\u9fff';

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
