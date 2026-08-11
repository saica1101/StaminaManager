using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.Views;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class SettingsBackupLocalizationContractTests
{
    [TestMethod]
    public void RestorePreviewLines_FormatsCurrentThenRestoredValues()
    {
        SettingsPage.RestorePreviewSnapshot current = new(
            AppTheme.Light,
            BackdropKind.Mica,
            true,
            CloseBehavior.MinimizeToTray,
            20,
            AppLanguage.Japanese,
            false);
        BackupPreview restored = new(
            5,
            3,
            AppTheme.Dark,
            BackdropKind.Acrylic,
            false,
            CloseBehavior.Exit,
            true,
            80,
            AppLanguage.English);

        CollectionAssert.AreEqual(
            new[]
            {
                "The current games, images, and settings will be replaced with the backup. Device-specific notification records will be kept.",
                "Games: 5",
                "Images: 3",
                "Theme: Light -> Dark",
                "Background: Mica -> Acrylic",
                "Notifications: Enabled -> Disabled",
                "Close behavior: Tray -> Exit",
                "Acrylic tint opacity: 20% -> 80%",
                "Language: Japanese -> English",
                "Startup: Disabled -> Enabled",
            },
            SettingsPage.CreateRestorePreviewLines(
                SettingsEnglishResourceFixture.Create(),
                current,
                restored).ToArray());
    }
    [TestMethod]
    public void RestoreDialog_ConnectsScrollablePreviewAndOpenedButtonAutomation()
    {
        string root = FindRepositoryRoot();
        string source = Read(root, "StaminaManager", "Views", "SettingsPage.xaml.cs");
        AssertContains(source,
            "Content = CreateRestorePreview(prepared.Preview)",
            "foreach (string line in CreateRestorePreviewLines(",
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
        int openedIndex = source.IndexOf(
            "confirmation.Opened += RestoreBackupDialog_Opened;",
            StringComparison.Ordinal);
        int handlerIndex = source.IndexOf(
            "private void RestoreBackupDialog_Opened",
            StringComparison.Ordinal);
        int handlerEnd = handlerIndex < 0
            ? -1
            : source.IndexOf(
                "private ScrollViewer CreateRestorePreview",
                handlerIndex,
                StringComparison.Ordinal);
        int applyIndex = handlerIndex < 0
            ? -1
            : source.IndexOf(
                "dialog.ApplyCloseButtonAutomation(",
                handlerIndex,
                StringComparison.Ordinal);
        Assert.IsTrue(openedIndex >= 0
            && handlerIndex > openedIndex
            && handlerEnd > handlerIndex
            && applyIndex > handlerIndex
            && applyIndex < handlerEnd);
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
        int cancelBlockStart = page.IndexOf(
            "if (result != ContentDialogResult.Primary)",
            StringComparison.Ordinal);
        int commitIndex = cancelBlockStart < 0
            ? -1
            : page.IndexOf(
                "phase = RestoreDialogPhase.Commit;",
                cancelBlockStart,
                StringComparison.Ordinal);
        string cancelBlock = cancelBlockStart >= 0 && commitIndex > cancelBlockStart
            ? page[cancelBlockStart..commitIndex]
            : string.Empty;
        int cancelIndex = cancelBlock.IndexOf(
            "phase = RestoreDialogPhase.Cancel;",
            StringComparison.Ordinal);
        int cancelAwaitIndex = cancelBlock.IndexOf(
            "await ViewModel.CancelPreparedRestoreAsync(",
            StringComparison.Ordinal);
        Assert.IsTrue(cancelIndex >= 0 && cancelAwaitIndex > cancelIndex);
        StringAssert.Contains(viewModel, "ReportBackupCancelFailure");
    }
    private static void AssertContains(
        string source,
        params string[] fragments)
    {
        foreach (string fragment in fragments)
        {
            StringAssert.Contains(source, fragment);
        }
    }
    private static string Read(string root, params string[] path) => File.ReadAllText(
        Path.Combine(new[] { root }.Concat(path).ToArray()));
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
