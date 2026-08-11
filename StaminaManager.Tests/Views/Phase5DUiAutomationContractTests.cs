namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class Phase5DUiAutomationContractTests
{
    [TestMethod]
    public void UiSuite_CoversLanguageRestartRoundTripAndVersionAboutOrder()
    {
        string suite = ReadUiScript("StaminaManager.UiTests.ps1");

        foreach (string fragment in new[]
        {
            "LanguageSelector",
            "VersionFooterBand",
            "VersionFooterText",
            "NavAbout",
            "Select-ComboItem LanguageSelector 'English'",
            "Select-ComboItem LanguageSelector '日本語'",
            "Start-PackagedApp",
            "VersionFooterAutomationNameFormat",
        })
        {
            StringAssert.Contains(suite, fragment);
        }
    }

    [TestMethod]
    public void UiSuite_UsesOnlyCurrentBackdropChoices()
    {
        string root = FindRepositoryRoot();
        foreach (string fileName in new[]
        {
            "StaminaManager.UiTests.ps1",
            "appearance-navigation-stress.ps1",
            "task9-appearance-integration.ps1",
        })
        {
            string script = File.ReadAllText(Path.Combine(
                root,
                "tests",
                "ui",
                fileName));
            Assert.DoesNotContain("Blur", script);
            Assert.DoesNotContain("Transparent", script);
            StringAssert.Contains(script, "Mica");
            StringAssert.Contains(script, "Acrylic");
            StringAssert.Contains(script, "Solid");
        }
    }

    [TestMethod]
    public void MutatingUiScripts_RejectStorePackagesBeforeLocalStateAccess()
    {
        string root = FindRepositoryRoot();
        foreach (string fileName in new[]
        {
            "StaminaManager.UiTests.ps1",
            "appearance-navigation-stress.ps1",
            "task9-appearance-integration.ps1",
        })
        {
            string script = File.ReadAllText(Path.Combine(
                root,
                "tests",
                "ui",
                fileName));
            StringAssert.Contains(script, "SignatureKind");
            StringAssert.Contains(script, "WindowsApps");
            StringAssert.Contains(script, "Store版");
        }
    }

    [TestMethod]
    public void UiSuite_RestartsAfterOpacityAndChecksPersistedValue()
    {
        string suite = ReadUiScript("StaminaManager.UiTests.ps1");

        int opacityIndex = suite.IndexOf(
            "Acrylic不透明度0/50/100",
            StringComparison.Ordinal);
        int restartIndex = suite.IndexOf(
            "Restart-TestPackage",
            opacityIndex,
            StringComparison.Ordinal);
        int persistenceIndex = suite.IndexOf(
            "Wait-PersistedAcrylicOpacity",
            restartIndex,
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, opacityIndex);
        Assert.IsGreaterThanOrEqualTo(0, restartIndex);
        Assert.IsGreaterThanOrEqualTo(0, persistenceIndex);
        Assert.IsGreaterThan(opacityIndex, restartIndex);
        Assert.IsGreaterThan(restartIndex, persistenceIndex);
    }

    private static string ReadUiScript(string fileName) => File.ReadAllText(
        Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "ui",
            fileName));

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
