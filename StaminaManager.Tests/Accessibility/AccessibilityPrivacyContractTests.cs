using Microsoft.UI.Xaml.Automation.Provider;
using StaminaManager.Controls;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;

namespace StaminaManager.Tests.Accessibility;

[TestClass]
public sealed class AccessibilityPrivacyContractTests
{
    private static readonly string[] InteractiveElementNames =
    [
        "Button",
        "ComboBox",
        "NavigationViewItem",
        "NumberBox",
        "TextBox",
        "ToggleSwitch",
    ];

    [TestMethod]
    public void InteractiveElements_HaveAutomationIdAndAccessibleName()
    {
        string root = FindRepositoryRoot();
        string appRoot = Path.Combine(root, "StaminaManager");
        HashSet<string> localizedNames = LoadLocalizedAutomationNames(
            appRoot);
        List<string> failures = [];

        foreach (string path in Directory.EnumerateFiles(
            appRoot,
            "*.xaml",
            SearchOption.AllDirectories)
            .Where(path => !path.Contains(
                $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                StringComparison.OrdinalIgnoreCase)))
        {
            XDocument document = XDocument.Load(path, LoadOptions.SetLineInfo);
            foreach (XElement element in document.Descendants()
                .Where(element => InteractiveElementNames.Contains(
                    element.Name.LocalName,
                    StringComparer.Ordinal)))
            {
                string? automationId = AttributeValue(
                    element,
                    "AutomationProperties.AutomationId");
                string? automationName = AttributeValue(
                    element,
                    "AutomationProperties.Name");
                string? uid = element.Attributes()
                    .FirstOrDefault(attribute =>
                        attribute.Name.LocalName == "Uid")?.Value;
                bool hasLocalizedName = uid is not null
                    && localizedNames.Contains(uid);
                IXmlLineInfo line = (IXmlLineInfo)element;
                string location = Path.GetRelativePath(root, path)
                    + $":{line.LineNumber} <{element.Name.LocalName}>";

                if (string.IsNullOrWhiteSpace(automationId))
                {
                    failures.Add(location + " に AutomationId がありません。");
                }

                if (string.IsNullOrWhiteSpace(automationName)
                    && !hasLocalizedName)
                {
                    failures.Add(location
                        + " に AccessibleName がありません。");
                }
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    [TestMethod]
    public void GameEditorDialog_HasDefaultAndCancelActions()
    {
        string root = FindRepositoryRoot();
        XElement dialog = XDocument.Load(Path.Combine(
                root,
                "StaminaManager",
                "Controls",
                "GameEditorDialog.xaml"))
            .Root
            ?? throw new AssertFailedException("ContentDialog がありません。");

        Assert.AreNotEqual(
            "None",
            AttributeValue(dialog, "DefaultButton"),
            "編集ダイアログには既定の操作が必要です。");
        Assert.IsFalse(
            string.IsNullOrWhiteSpace(AttributeValue(
                dialog,
                "CloseButtonText")),
            "編集ダイアログには取消または閉じる操作が必要です。");
    }

    [TestMethod]
    public void GameEditorDeleteConfirmation_UsesSafeDefaultAndPreservesInput()
    {
        string root = FindRepositoryRoot();
        string source = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Controls",
            "GameEditorDialog.xaml.cs"));

        StringAssert.Contains(source, "DefaultButton = isDeleteConfirmation");
        StringAssert.Contains(source, "? ContentDialogButton.Close");
        StringAssert.Contains(source, "BackToEditingCommand.Execute(null)");
        StringAssert.Contains(source, "await ViewModel.DeleteAsync");
    }

    [TestMethod]
    public void StaminaRing_ExposesReadOnlyRangeValue()
    {
        MethodInfo? peerFactory = typeof(StaminaRing).GetMethod(
            "OnCreateAutomationPeer",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(peerFactory);
        Assert.AreEqual(
            typeof(StaminaRing),
            peerFactory.DeclaringType,
            "StaminaRing 自身が専用 AutomationPeer を生成する必要があります。");

        Type? peerType = typeof(StaminaRing).Assembly.GetType(
            "StaminaManager.Controls.StaminaRingAutomationPeer");
        Assert.IsNotNull(
            peerType,
            "StaminaRing 専用 AutomationPeer が必要です。");
        Assert.IsTrue(typeof(IRangeValueProvider).IsAssignableFrom(peerType));

        MethodInfo createInfo = peerType.GetMethod(
            "CreateInfo",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new AssertFailedException(
                "読み上げ情報の生成メソッドがありません。");
        object info = createInfo.Invoke(
            null,
            ["テストゲーム", 75, 200, 0.375, "余裕"])
            ?? throw new AssertFailedException(
                "読み上げ情報を生成できませんでした。");

        Assert.AreEqual(0d, Property<double>(info, "Minimum"));
        Assert.AreEqual(200d, Property<double>(info, "Maximum"));
        Assert.AreEqual(75d, Property<double>(info, "Value"));
        StringAssert.Contains(Property<string>(info, "Name"), "75 / 200");
        StringAssert.Contains(Property<string>(info, "HelpText"), "38%");
    }

    [TestMethod]
    public void PrivacyAndOfflineContract_IsDocumentedAndEnforced()
    {
        string root = FindRepositoryRoot();
        string privacyPath = Path.Combine(root, "docs", "privacy.md");
        Assert.IsTrue(File.Exists(privacyPath), "docs/privacy.md が必要です。");
        string privacy = File.ReadAllText(privacyPath);
        foreach (string requiredText in new[]
        {
            "アカウント",
            "広告",
            "計測",
            "送信しません",
            "ApplicationData",
            "通知",
            "StartupTask",
            "バックアップ",
            "アンインストール",
        })
        {
            StringAssert.Contains(privacy, requiredText);
        }

        string manifest = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Package.appxmanifest"));
        Assert.IsFalse(manifest.Contains(
            "internetClient",
            StringComparison.OrdinalIgnoreCase));

        string source = string.Join(
            Environment.NewLine,
            Directory.EnumerateFiles(
                    Path.Combine(root, "StaminaManager"),
                    "*.cs",
                    SearchOption.AllDirectories)
                .Concat(Directory.EnumerateFiles(
                    Path.Combine(root, "StaminaManager.Core"),
                    "*.cs",
                    SearchOption.AllDirectories))
                .Select(File.ReadAllText));
        foreach (string forbidden in new[]
        {
            "HttpClient",
            "WebRequest",
            "Socket",
            "Telemetry",
            "Analytics",
            "Crash",
        })
        {
            Assert.IsFalse(
                source.Contains(forbidden, StringComparison.Ordinal),
                $"オフライン契約に反する識別子 {forbidden} があります。");
        }
    }

    [TestMethod]
    public void BackupDescription_DoesNotClaimImplementedFeatureIsPending()
    {
        string root = FindRepositoryRoot();
        string resources = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Resources",
            "Strings",
            "ja-JP",
            "Resources.resw"));

        Assert.IsFalse(resources.Contains(
            "現在は準備中",
            StringComparison.Ordinal));
        StringAssert.Contains(resources, "アンインストール");
        StringAssert.Contains(resources, "バックアップ");
    }

    [TestMethod]
    public void ImportRejection_ShowsSpecificRecoveryAction()
    {
        string root = FindRepositoryRoot();
        string pageSource = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Views",
            "SettingsPage.xaml.cs"));
        string viewModelSource = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "ViewModels",
            "SettingsViewModel.cs"));

        StringAssert.Contains(
            pageSource,
            "ReportBackupImportFailure");
        StringAssert.Contains(
            viewModelSource,
            "別のバックアップを選んで再試行してください");
    }

    [TestMethod]
    public void RestoreDialogActions_HaveStableAutomationIds()
    {
        string root = FindRepositoryRoot();
        string styles = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Resources",
            "Styles.xaml"));
        string pageSource = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Views",
            "SettingsPage.xaml.cs"));

        foreach (string requiredText in new[]
        {
            "RestoreDialogPrimaryButtonStyle",
            "RestoreBackupConfirmButton",
            "RestoreDialogCancelButtonStyle",
            "RestoreBackupCancelButton",
        })
        {
            StringAssert.Contains(styles, requiredText);
        }

        StringAssert.Contains(pageSource, "RestoreDialogPrimaryButtonStyle");
        StringAssert.Contains(pageSource, "RestoreDialogCancelButtonStyle");
        StringAssert.Contains(pageSource, "RestoreBackupDialog");
    }

    [TestMethod]
    public void RecoveryInfoBar_HasAccessiblePersistentContract()
    {
        string root = FindRepositoryRoot();
        XElement infoBar = XDocument.Load(Path.Combine(
                root,
                "StaminaManager",
                "Views",
                "OverviewPage.xaml"))
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "InfoBar"
                && AttributeValue(element, "Name") == "RecoveryInfoBar");

        Assert.AreEqual(
            "OverviewRecoveryInfoBar",
            AttributeValue(infoBar, "AutomationProperties.AutomationId"));
        Assert.AreEqual(
            "Polite",
            AttributeValue(infoBar, "AutomationProperties.LiveSetting"));
        Assert.AreEqual("True", AttributeValue(infoBar, "IsClosable"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(
            AttributeValue(infoBar, "Closed")));

        HashSet<string> localizedNames = LoadLocalizedAutomationNames(
            Path.Combine(root, "StaminaManager"));
        Assert.Contains("OverviewRecoveryInfoBar", localizedNames);
    }

    [TestMethod]
    public void OverviewItems_AllowsSequentialTabNavigationWithinCards()
    {
        string root = FindRepositoryRoot();
        XElement itemsRepeater = XDocument.Load(Path.Combine(
                root,
                "StaminaManager",
                "Views",
                "OverviewPage.xaml"))
            .Descendants()
            .Single(element =>
                element.Name.LocalName == "ItemsRepeater"
                && AttributeValue(element, "Name") == "OverviewItems");

        Assert.AreEqual(
            "Local",
            AttributeValue(itemsRepeater, "TabFocusNavigation"),
            "ゲームカードと追加カードを視覚順にTab移動できる必要があります。");
    }

    private static string? AttributeValue(
        XElement element,
        string localName) => element.Attributes()
            .FirstOrDefault(attribute =>
                attribute.Name.LocalName == localName)?.Value;

    private static T Property<T>(object instance, string name) =>
        (T)(instance.GetType().GetProperty(name)?.GetValue(instance)
            ?? throw new AssertFailedException(
                $"{name} を取得できませんでした。"));

    private static HashSet<string> LoadLocalizedAutomationNames(
        string appRoot)
    {
        XDocument resources = XDocument.Load(Path.Combine(
            appRoot,
            "Resources",
            "Strings",
            "ja-JP",
            "Resources.resw"));
        const string suffix =
            ".[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name";
        return resources.Root!
            .Elements("data")
            .Select(element => (string?)element.Attribute("name"))
            .Where(name => name?.EndsWith(
                suffix,
                StringComparison.Ordinal) == true)
            .Select(name => name![..^suffix.Length])
            .ToHashSet(StringComparer.Ordinal);
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
