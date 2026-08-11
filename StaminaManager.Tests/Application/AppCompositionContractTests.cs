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
                languageService,
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
