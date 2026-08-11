using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Resources;
using StaminaManager.Views;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class SettingsBackupLocalizationContractTests
{
    [TestMethod]
    public void RestorePreviewFormatters_KeepCurrentAndRestoredValuesDistinct()
    {
        IAppResourceService resources = new AppResourceService(ResolveFormatterResource);

        CollectionAssert.AreEqual(
            new[]
            {
                "Theme: Light -> Dark",
                "Theme: Dark -> Light",
                "Background: Mica -> Acrylic",
                "Notifications: Enabled -> Disabled",
                "Close: Tray -> Exit",
                "Language: Japanese -> English",
            },
            new[]
            {
                Line(resources, "SettingsRestorePreviewThemeFormat",
                    SettingsPage.FormatTheme(resources, AppTheme.Light),
                    SettingsPage.FormatTheme(resources, AppTheme.Dark)),
                Line(resources, "SettingsRestorePreviewThemeFormat",
                    SettingsPage.FormatTheme(resources, AppTheme.Dark),
                    SettingsPage.FormatTheme(resources, AppTheme.Light)),
                Line(resources, "SettingsRestorePreviewBackdropFormat",
                    SettingsPage.FormatBackdrop(resources, BackdropKind.Mica),
                    SettingsPage.FormatBackdrop(resources, BackdropKind.Acrylic)),
                Line(resources, "SettingsRestorePreviewNotificationsFormat",
                    SettingsPage.FormatEnabled(resources, true),
                    SettingsPage.FormatEnabled(resources, false)),
                Line(resources, "SettingsRestorePreviewCloseBehaviorFormat",
                    SettingsPage.FormatCloseBehavior(
                        resources, CloseBehavior.MinimizeToTray),
                    SettingsPage.FormatCloseBehavior(
                        resources, CloseBehavior.Exit)),
                Line(resources, "SettingsRestorePreviewLanguageFormat",
                    SettingsPage.FormatLanguage(resources, AppLanguage.Japanese),
                    SettingsPage.FormatLanguage(resources, AppLanguage.English)),
            });
    }
    [TestMethod]
    public void RestoreDialog_ConnectsScrollablePreviewAndOpenedButtonAutomation()
    {
        string root = FindRepositoryRoot();
        string source = Read(root, "StaminaManager", "Views", "SettingsPage.xaml.cs");
        AssertContains(source,
            "Content = CreateRestorePreview(prepared.Preview)",
            "new ScrollViewer",
            "MaxHeight = 360",
            "VerticalScrollBarVisibility = ScrollBarVisibility.Auto",
            "HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled",
            "Opened += RestoreBackupDialog_Opened",
            "GetTemplateChild(\"CloseButton\")",
            "AutomationProperties.SetAutomationId",
            "AutomationProperties.SetName",
            "AutomationProperties.SetHelpText",
            "DefaultButton = ContentDialogButton.Close");

        string styles = Read(root, "StaminaManager", "Resources", "Styles.xaml");
        int primaryStart = styles.IndexOf(
            "x:Key=\"RestoreDialogPrimaryButtonStyle\"",
            StringComparison.Ordinal);
        int cancelStart = styles.IndexOf(
            "x:Key=\"RestoreDialogCancelButtonStyle\"",
            StringComparison.Ordinal);
        Assert.IsTrue(primaryStart >= 0 && cancelStart > primaryStart);
        StringAssert.Contains(
            styles[primaryStart..cancelStart],
            "BasedOn=\"{StaticResource DefaultButtonStyle}\"");
        StringAssert.Contains(
            styles[cancelStart..],
            "BasedOn=\"{StaticResource AccentButtonStyle}\"");
    }
    [TestMethod]
    public void RestoreDialog_CancelFailureHasOnePhaseOwner()
    {
        string root = FindRepositoryRoot();
        string page = Read(root, "StaminaManager", "Views", "SettingsPage.xaml.cs");
        string viewModel = Read(root, "StaminaManager", "ViewModels", "SettingsViewModel.cs");

        AssertContains(page,
            "RestoreDialogPhase",
            "RestoreDialogPhase.Cancel",
            "phase == RestoreDialogPhase.Preview",
            "phase == RestoreDialogPhase.Commit");
        StringAssert.Contains(viewModel, "ReportBackupCancelFailure");
    }
    private static string Line(
        IAppResourceService resources,
        string resourceId,
        params object?[] args) => resources.Format(resourceId, args);

    private static void AssertContains(
        string source,
        params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            StringAssert.Contains(source, fragment);
        }
    }
    private static string Read(string root, params string[] path) =>
        File.ReadAllText(Path.Combine(
            new[] { root }.Concat(path).ToArray()));
    private static string ResolveFormatterResource(string resourceId) =>
        resourceId switch
        {
            "SettingsRestorePreviewOn" => "Enabled",
            "SettingsRestorePreviewOff" => "Disabled",
            "SettingsRestorePreviewLight" => "Light",
            "SettingsRestorePreviewDark" => "Dark",
            "SettingsRestorePreviewMica" => "Mica",
            "SettingsRestorePreviewAcrylic" => "Acrylic",
            "SettingsRestorePreviewTray" => "Tray",
            "SettingsRestorePreviewExit" => "Exit",
            "SettingsRestorePreviewJapanese" => "Japanese",
            "SettingsRestorePreviewEnglish" => "English",
            "SettingsRestorePreviewThemeFormat" => "Theme: {0} -> {1}",
            "SettingsRestorePreviewBackdropFormat" =>
                "Background: {0} -> {1}",
            "SettingsRestorePreviewNotificationsFormat" =>
                "Notifications: {0} -> {1}",
            "SettingsRestorePreviewCloseBehaviorFormat" =>
                "Close: {0} -> {1}",
            "SettingsRestorePreviewLanguageFormat" =>
                "Language: {0} -> {1}",
            _ => resourceId,
        };

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(
                directory.FullName,
                "StaminaManager.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new AssertFailedException(
                "リポジトリルートを検出できません。");
    }
}
