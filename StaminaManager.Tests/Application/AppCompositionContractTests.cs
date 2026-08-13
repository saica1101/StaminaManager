using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Resources;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class AppCompositionContractTests
{
    [TestMethod]
    public void SessionResources_ResolveLanguageOnceAndShareSameValue()
    {
        CountingLanguageService languageService = new(
            AppLanguage.English);
        AppLanguage? resourceLanguage = null;

        (
            IAppResourceService ResourceService,
            AppLanguage SessionLanguage) session =
            global::StaminaManager.App.CreateSessionResources(
                languageService.GetEffectiveLanguage,
                language =>
                {
                    resourceLanguage = language;
                    return new AppResourceService(resourceId => resourceId);
                });

        Assert.AreEqual(1, languageService.GetEffectiveLanguageCallCount);
        Assert.AreEqual(AppLanguage.English, resourceLanguage);
        Assert.AreEqual(AppLanguage.English, session.SessionLanguage);
        Assert.IsNotNull(session.ResourceService);
    }

    [TestMethod]
    public void Launch_RefreshesVersionFooterAfterInitializationBeforeActivation()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "App.xaml.cs"));
        int initialization = source.IndexOf(
            "DataLoadResult loadResult = await InitializeForLaunchAsync(",
            StringComparison.Ordinal);
        int refresh = source.IndexOf(
            "_mainPage!.RefreshVersionFooterAutomationProperties();",
            Math.Max(initialization, 0),
            StringComparison.Ordinal);
        int attach = source.IndexOf(
            "ActivationRouter.Attach(HandleRedirectedActivation);",
            Math.Max(refresh, 0),
            StringComparison.Ordinal);
        int activate = source.IndexOf(
            "_window!.Activate();",
            Math.Max(refresh, 0),
            StringComparison.Ordinal);
        int failureCheck = source.IndexOf(
            "if (loadResult.Status == DataLoadStatus.Corrupt",
            Math.Max(refresh, 0),
            StringComparison.Ordinal);

        Assert.IsGreaterThanOrEqualTo(0, initialization);
        Assert.IsGreaterThan(initialization, refresh);
        Assert.IsGreaterThan(refresh, attach);
        Assert.IsGreaterThan(refresh, activate);
        Assert.IsGreaterThan(refresh, failureCheck);
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

    private sealed class CountingLanguageService(AppLanguage language)
        : IAppLanguageService
    {
        public int GetEffectiveLanguageCallCount { get; private set; }

        public AppLanguage GetEffectiveLanguage()
        {
            GetEffectiveLanguageCallCount++;
            return language;
        }

        public LanguageChangeResult SetLanguage(AppLanguage language) =>
            new(language, IsApplied: true, LanguageFailureReason.None);
    }
}
