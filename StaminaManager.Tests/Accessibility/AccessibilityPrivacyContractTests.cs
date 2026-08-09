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
        "Slider",
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
    public void GameEditorDialog_テーマ継承と回復時間見出しを持つ()
    {
        string root = FindRepositoryRoot();
        string mainPageSource = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "MainPage.xaml.cs"));
        string dialogXaml = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Controls",
            "GameEditorDialog.xaml"));

        StringAssert.Contains(
            mainPageSource,
            "RequestedTheme = ActualTheme");
        StringAssert.Contains(
            dialogXaml,
            "x:Uid=\"RecoveryIntervalHeading\"");
        StringAssert.Contains(dialogXaml, "<RowDefinition Height=\"Auto\" />");
    }

    [TestMethod]
    public void GameEditorRecoveryLabels_UseMeaningfulLocalizedValues()
    {
        string root = FindRepositoryRoot();
        foreach ((string language, string heading, string minutes, string seconds) in new[]
        {
            ("ja-JP", "スタミナが1回復する時間", "分", "秒"),
            ("en-US", "Time to recover one stamina", "Minutes", "Seconds"),
        })
        {
            Dictionary<string, string> values = LoadResourceValues(
                Path.Combine(root, "StaminaManager"),
                language);

            Assert.AreEqual(
                heading,
                values.GetValueOrDefault("RecoveryIntervalHeading.Text"));
            Assert.AreEqual(
                minutes,
                values.GetValueOrDefault("RecoveryMinutesInput.Header"));
            Assert.AreEqual(
                seconds,
                values.GetValueOrDefault("RecoverySecondsInput.Header"));
        }
    }

    [TestMethod]
    public void GameEditorDeleteActions_共通の破壊的スタイルを使う()
    {
        string root = FindRepositoryRoot();
        string styles = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Resources",
            "Styles.xaml"));
        string dialog = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Controls",
            "GameEditorDialog.xaml"));

        foreach (string requiredText in new[]
        {
            "AppDestructiveButtonStyle",
            "DestructiveButtonBackgroundBrush",
            "DestructiveButtonBackgroundPointerOverBrush",
            "DestructiveButtonBackgroundPressedBrush",
            "DestructiveButtonBackgroundDisabledBrush",
        })
        {
            StringAssert.Contains(styles, requiredText);
        }

        StringAssert.Contains(
            dialog,
            "BasedOn=\"{StaticResource AppDestructiveButtonStyle}\"");
        StringAssert.Contains(
            dialog,
            "Style=\"{StaticResource AppDestructiveButtonStyle}\"");
        StringAssert.Contains(dialog, "GameEditorDeleteButton");
        StringAssert.Contains(dialog, "DeleteConfirmButton");
    }

    [TestMethod]
    public void DestructiveButton_HighContrastでも操作状態を区別できる()
    {
        string root = FindRepositoryRoot();
        string tokensPath = Path.Combine(
            root,
            "StaminaManager",
            "Resources",
            "DesignTokens.xaml");
        XDocument tokens = XDocument.Load(tokensPath);
        XElement highContrast = tokens.Descendants()
            .Single(element =>
                element.Name.LocalName == "ResourceDictionary"
                && AttributeValue(element, "Key") == "HighContrast");
        string[] stateKeys =
        [
            "DestructiveButtonBackgroundBrush",
            "DestructiveButtonBackgroundPointerOverBrush",
            "DestructiveButtonBackgroundPressedBrush",
        ];
        string[] systemBrushes = stateKeys
            .Select(key => highContrast.Elements()
                .Single(element => AttributeValue(element, "Key") == key)
                .Attributes()
                .Single(attribute =>
                    attribute.Name.LocalName == "ResourceKey")
                .Value)
            .ToArray();

        Assert.AreEqual(
            stateKeys.Length,
            systemBrushes.Distinct(StringComparer.Ordinal).Count());

        string styles = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Resources",
            "Styles.xaml"));
        StringAssert.Contains(
            styles,
            "DestructiveButtonForegroundPointerOverBrush");
        StringAssert.Contains(
            styles,
            "DestructiveButtonForegroundPressedBrush");
    }

    [TestMethod]
    public void GameEditorImageGuidance_5MB制限を表示しない()
    {
        string root = FindRepositoryRoot();
        string dialog = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Controls",
            "GameEditorDialog.xaml"));
        string code = File.ReadAllText(Path.Combine(
            root,
            "StaminaManager",
            "Controls",
            "GameEditorDialog.xaml.cs"));
        string resources = string.Join(
            Environment.NewLine,
            new[] { "ja-JP", "en-US" }
                .Select(language => File.ReadAllText(Path.Combine(
                    root,
                    "StaminaManager",
                    "Resources",
                    "Strings",
                    language,
                    "Resources.resw"))));

        string[] forbiddenImageSizeText = ["5MB", "5 MB", "5 MiB"];
        string combined = dialog + code + resources;
        foreach (string forbiddenText in forbiddenImageSizeText)
        {
            Assert.DoesNotContain(forbiddenText, combined);
        }

        foreach (string language in new[] { "ja-JP", "en-US" })
        {
            string guidance = LoadResourceValues(
                Path.Combine(root, "StaminaManager"),
                language)
                .GetValueOrDefault("SelectedImageText.Text")
                ?? string.Empty;
            StringAssert.Contains(guidance, "4096×4096");
            foreach (string forbiddenText in forbiddenImageSizeText)
            {
                Assert.DoesNotContain(forbiddenText, guidance);
            }
        }

        StringAssert.Contains(dialog, "x:Uid=\"SelectedImageText\"");
    }

    [TestMethod]
    public void GameEditorRecoveryErrors_AreAssociatedWithBothInputs()
    {
        string root = FindRepositoryRoot();
        XDocument dialog = XDocument.Load(Path.Combine(
            root,
            "StaminaManager",
            "Controls",
            "GameEditorDialog.xaml"));
        Dictionary<string, string> expectedBindings = new()
        {
            ["RecoveryMinutesInput"] =
                "FirstNonEmptyError(ViewModel.RecoveryMinutesError, "
                + "ViewModel.RecoveryIntervalError)",
            ["RecoverySecondsInput"] =
                "FirstNonEmptyError(ViewModel.RecoverySecondsError, "
                + "ViewModel.RecoveryIntervalError)",
        };

        foreach ((string automationId, string expectedBinding) in
            expectedBindings)
        {
            XElement input = dialog.Descendants().Single(element =>
                element.Name.LocalName == "NumberBox"
                && AttributeValue(
                    element,
                    "AutomationProperties.AutomationId") == automationId);
            StringAssert.Contains(
                AttributeValue(input, "AutomationProperties.HelpText")
                    ?? string.Empty,
                expectedBinding);
        }

        MethodInfo? selector = typeof(GameEditorDialog).GetMethod(
            "FirstNonEmptyError",
            BindingFlags.Public | BindingFlags.Static);
        Assert.IsNotNull(selector);
        const string fieldError = "field error";
        const string intervalError = "interval error";
        Assert.AreEqual(
            fieldError,
            selector.Invoke(null, [fieldError, intervalError]));
        Assert.AreEqual(
            intervalError,
            selector.Invoke(null, [null, intervalError]));
        Assert.AreEqual(
            intervalError,
            selector.Invoke(null, [" ", intervalError]));
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
    public void SettingsLabels_指定された日本語と操作名を使う()
    {
        string root = FindRepositoryRoot();
        XDocument document = XDocument.Load(Path.Combine(
            root,
            "StaminaManager",
            "Resources",
            "Strings",
            "ja-JP",
            "Resources.resw"));
        Dictionary<string, string> values = document.Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => element.Element("value")?.Value ?? string.Empty,
                StringComparer.Ordinal);

        Assert.AreEqual("外観", values["SettingsAppearanceHeading.Text"]);
        Assert.AreEqual("動作", values["SettingsGeneralHeading.Text"]);
        Assert.AreEqual("通知", values["SettingsNotificationsHeading.Text"]);
        Assert.AreEqual("バックアップ", values["SettingsDataHeading.Text"]);
        Assert.AreEqual("エクスポート", values["ExportBackupButton.Content"]);
        Assert.AreEqual("インポート", values["ImportBackupButton.Content"]);
        Assert.AreEqual(
            "バックアップをエクスポートする",
            values["ExportBackupButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"]);
        Assert.AreEqual(
            "バックアップをインポートして現在のデータを置き換える",
            values["ImportBackupButton.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name"]);
        Assert.DoesNotContain(
            "バックアップを書き出す",
            string.Join('\n', values.Values));
        Assert.DoesNotContain(
            "バックアップから置き換える",
            string.Join('\n', values.Values));
    }

    [TestMethod]
    public void AcrylicOpacitySlider_HasLocalizedAccessibilityAndValueText()
    {
        string root = FindRepositoryRoot();
        XDocument page = XDocument.Load(Path.Combine(
            root,
            "StaminaManager",
            "Views",
            "SettingsPage.xaml"));
        XElement slider = page.Descendants()
            .Single(element => element.Name.LocalName == "Slider"
                && AttributeValue(
                    element,
                    "AutomationProperties.AutomationId")
                    == "AcrylicOpacitySlider");

        Assert.AreEqual(
            "AcrylicOpacitySlider",
            AttributeValue(slider, "Uid"));
        Assert.AreEqual("0", AttributeValue(slider, "Minimum"));
        Assert.AreEqual("100", AttributeValue(slider, "Maximum"));
        Assert.AreEqual("1", AttributeValue(slider, "StepFrequency"));
        Assert.AreEqual("10", AttributeValue(slider, "TickFrequency"));
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(
                root,
                "StaminaManager",
                "Resources",
                "Strings",
                "ja-JP",
                "Resources.resw")),
            "AcrylicOpacitySlider.[using:Microsoft.UI.Xaml.Automation]AutomationProperties.HelpText");
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(
                root,
                "StaminaManager",
                "Views",
                "SettingsPage.xaml")),
            "AcrylicOpacityValueText");
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

    private static Dictionary<string, string> LoadResourceValues(
        string appRoot,
        string language)
    {
        XDocument resources = XDocument.Load(Path.Combine(
            appRoot,
            "Resources",
            "Strings",
            language,
            "Resources.resw"));
        return resources.Root!
            .Elements("data")
            .ToDictionary(
                element => (string)element.Attribute("name")!,
                element => (string?)element.Element("value") ?? string.Empty,
                StringComparer.OrdinalIgnoreCase);
    }

    private static HashSet<string> LoadLocalizedAutomationNames(
        string appRoot)
    {
        const string suffix =
            ".[using:Microsoft.UI.Xaml.Automation]AutomationProperties.Name";
        return LoadResourceValues(appRoot, "ja-JP")
            .Keys
            .Where(name => name.EndsWith(
                suffix,
                StringComparison.Ordinal) == true)
            .Select(name => name[..^suffix.Length])
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
