using System.Xml.Linq;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class SettingsBackupLocalizationContractTests
{
    [TestMethod]
    public void SettingsBackupFlow_UsesTheAppScopedResourceService()
    {
        string root = FindRepositoryRoot();
        string page = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Views",
            "SettingsPage.xaml.cs"));
        string viewModel = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "ViewModels",
            "SettingsViewModel.cs"));

        StringAssert.Contains(page, "SettingsPage(");
        StringAssert.Contains(page, "IAppResourceService appResourceService");
        StringAssert.Contains(page, "_appResourceService.GetString");
        StringAssert.Contains(page, "_appResourceService.Format");
        foreach (string literal in new[]
        {
            "バックアップを復元しますか？",
            "現在データを置き換える",
            "現在のゲーム、画像、設定をバックアップの内容で置き換えます。",
            "ゲーム: {preview.GameCount}件",
        })
        {
            Assert.DoesNotContain(literal, page);
        }

        foreach (string literal in new[]
        {
            "バックアップ処理中です。完了してからもう一度お試しください。",
            "バックアップを作成しています…",
            "バックアップを復元しています…",
            "選択したバックアップを読み込めませんでした。",
        })
        {
            Assert.DoesNotContain(literal, viewModel);
        }
    }

    [TestMethod]
    public void RestorePreview_FormatsEveryTypedSettingThroughResources()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "Views",
            "SettingsPage.xaml.cs"));

        foreach (string resourceId in new[]
        {
            "SettingsRestorePreviewDescription",
            "SettingsRestorePreviewGameCountFormat",
            "SettingsRestorePreviewImageCountFormat",
            "SettingsRestorePreviewThemeFormat",
            "SettingsRestorePreviewBackdropFormat",
            "SettingsRestorePreviewNotificationsFormat",
            "SettingsRestorePreviewCloseBehaviorFormat",
            "SettingsRestorePreviewAcrylicOpacityFormat",
            "SettingsRestorePreviewLanguageFormat",
            "SettingsRestorePreviewStartupFormat",
            "SettingsRestorePreviewOn",
            "SettingsRestorePreviewOff",
            "SettingsRestorePreviewLight",
            "SettingsRestorePreviewDark",
            "SettingsRestorePreviewMica",
            "SettingsRestorePreviewAcrylic",
            "SettingsRestorePreviewSolid",
            "SettingsRestorePreviewBlur",
            "SettingsRestorePreviewTransparent",
            "SettingsRestorePreviewTray",
            "SettingsRestorePreviewExit",
            "SettingsRestorePreviewJapanese",
            "SettingsRestorePreviewEnglish",
        })
        {
            StringAssert.Contains(source, resourceId);
        }
    }

    [TestMethod]
    public void RestoreDialog_UsesLocalizedLabelsAndAccessibleText()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Views",
            "SettingsPage.xaml.cs"));
        Dictionary<string, string> japanese = LoadResources(root, "ja-JP");
        Dictionary<string, string> english = LoadResources(root, "en-US");

        foreach (string key in new[]
        {
            "SettingsRestoreDialogTitle",
            "SettingsRestoreDialogPrimaryButton",
            "SettingsRestoreDialogCloseButton",
            "SettingsRestoreDialogAutomationName",
            "SettingsRestoreDialogHelpText",
        })
        {
            StringAssert.Contains(source, key);
            Assert.IsTrue(japanese.ContainsKey(key), key);
            Assert.IsTrue(english.ContainsKey(key), key);
        }
    }

    private static Dictionary<string, string> LoadResources(
        string root,
        string language)
    {
        XElement[] entries = XDocument.Load(Path.Combine(
                root,
                "StaminaManager",
                "Resources",
                "Strings",
                language,
                "Resources.resw"))
            .Root!
            .Elements("data")
            .ToArray();
        return entries.ToDictionary(
            entry => (string)entry.Attribute("name")!,
            entry => entry.Element("value")?.Value ?? string.Empty,
            StringComparer.Ordinal);
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
            "リポジトリルートを検出できません。");
    }
}
