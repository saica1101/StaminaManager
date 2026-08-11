using StaminaManager.Core.Abstractions;
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
            new ThrowingResourceService(
                new InvalidOperationException("private loader detail")),
            AppLanguage.English);

        Assert.AreEqual("Could not start Stamina Manager", text.Heading);
        Assert.AreEqual(
            "A problem occurred during startup. Close the app and try again.",
            text.Message);
        Assert.DoesNotContain("private loader detail", text.Message);
    }

    [TestMethod]
    public void LoadLaunchFailureText_WhenResourceThrowsOutOfMemory_Propagates()
    {
        Assert.ThrowsExactly<OutOfMemoryException>(() =>
            App.LoadLaunchFailureText(
                new ThrowingResourceService(
                    new OutOfMemoryException("private loader detail")),
                AppLanguage.English));
    }

    [TestMethod]
    public void StartupFailureAutomationAndFocusContract_IsLocalizedAndOrdered()
    {
        string method = ExtractFallbackMethod();

        StringAssert.Contains(
            method,
            "AutomationProperties.SetAutomationId(");
        StringAssert.Contains(method, "\"StartupFailureCloseButton\"");
        StringAssert.Contains(
            method,
            "AutomationProperties.SetName(closeButton, text.CloseButton);");

        int activateIndex = method.LastIndexOf(
            "fallbackWindow.Activate();",
            StringComparison.Ordinal);
        int focusIndex = method.IndexOf(
            "closeButton.Focus(FocusState.Programmatic);",
            StringComparison.Ordinal);
        Assert.IsTrue(
            activateIndex >= 0 && focusIndex > activateIndex,
            "fallback windowのActivate後にclose buttonへFocusしてください。");
    }

    private static string ExtractFallbackMethod()
    {
        string source = File.ReadAllText(FindAppSource());
        const string startMarker = "private bool TryShowFallbackWindow()";
        const string endMarker =
            "internal static LaunchFailureText LoadLaunchFailureText(";
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.IsGreaterThanOrEqualTo(0, start);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.IsGreaterThan(
            start,
            end,
            "起動失敗fallbackメソッドの範囲を検出できません。");
        return source[start..end];
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

    private sealed class ThrowingResourceService(Exception exception)
        : IAppResourceService
    {
        public string GetString(string resourceId) => throw exception;

        public string Format(string resourceId, params object?[] args) =>
            throw exception;
    }
}
