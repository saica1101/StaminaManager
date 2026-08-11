using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Resources;
using StaminaManager;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class AppStartupFailureTextTests
{
    [TestMethod]
    public void LoadLaunchFailureText_UsesJapaneseSessionResources()
    {
        App.LaunchFailureText text = App.LoadLaunchFailureText(
            new AppResourceService(resourceId => resourceId switch
            {
                "StartupFailureWindowTitle" => "Stamina Manager",
                "StartupFailureHeading" => "日本語の起動失敗見出し",
                "StartupFailureMessage" => "日本語の起動失敗本文",
                "StartupFailureCloseButton" => "閉じる",
                _ => resourceId,
            }),
            AppLanguage.Japanese);

        Assert.AreEqual("Stamina Manager", text.WindowTitle);
        Assert.AreEqual("日本語の起動失敗見出し", text.Heading);
        Assert.AreEqual("日本語の起動失敗本文", text.Message);
        Assert.AreEqual("閉じる", text.CloseButton);
    }

    [TestMethod]
    public void LoadLaunchFailureText_UsesEnglishSessionFallbackWhenResourcesAreMissing()
    {
        App.LaunchFailureText text = App.LoadLaunchFailureText(
            new AppResourceService(_ => string.Empty),
            AppLanguage.English);

        Assert.AreEqual("Stamina Manager", text.WindowTitle);
        Assert.AreEqual("Could not start Stamina Manager", text.Heading);
        Assert.AreEqual(
            "A problem occurred during startup. Close the app and try again.",
            text.Message);
        Assert.AreEqual("Close", text.CloseButton);
        Assert.DoesNotContain("StartupFailure", text.Heading);
        Assert.DoesNotContain("起動", text.Heading);
        Assert.DoesNotContain("StartupFailure", text.Message);
    }

    [TestMethod]
    public void LoadLaunchFailureText_UsesLanguageFallbackWhenResourceLoaderThrows()
    {
        App.LaunchFailureText text = App.LoadLaunchFailureText(
            new AppResourceService(
                _ => throw new InvalidOperationException("private loader detail")),
            AppLanguage.English);

        Assert.AreEqual("Could not start Stamina Manager", text.Heading);
        Assert.AreEqual(
            "A problem occurred during startup. Close the app and try again.",
            text.Message);
        Assert.DoesNotContain("private loader detail", text.Message);
    }

    [TestMethod]
    public void StartupFailureAutomationName_UsesLocalizedCloseButton()
    {
        string source = File.ReadAllText(FindAppSource());

        StringAssert.Contains(
            source,
            "AutomationProperties.SetName(closeButton, text.CloseButton);");
        StringAssert.Contains(source, "closeButton.Focus(FocusState.Programmatic);");
    }

    private static string FindAppSource() => Path.Combine(
        FindRepositoryRoot(),
        "StaminaManager",
        "App.xaml.cs");

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "StaminaManager.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new AssertFailedException("リポジトリ ルートを検出できません。");
    }
}
