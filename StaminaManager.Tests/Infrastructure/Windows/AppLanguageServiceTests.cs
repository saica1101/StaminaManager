using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class AppLanguageServiceTests
{
    [TestMethod]
    public void GetEffectiveLanguage_対応Overrideを最優先する()
    {
        AppLanguageService service = new(
            () => "en-US",
            () => ["ja-JP"]);

        Assert.AreEqual(AppLanguage.English, service.GetEffectiveLanguage());
    }

    [TestMethod]
    public void GetEffectiveLanguage_Override非対応時は最初の対応OS言語を使う()
    {
        AppLanguageService service = new(
            () => "fr-FR",
            () => ["de-DE", "en-US", "ja-JP"]);

        Assert.AreEqual(AppLanguage.English, service.GetEffectiveLanguage());
    }

    [TestMethod]
    public void GetEffectiveLanguage_対応言語なしではJapaneseへFallbackする()
    {
        AppLanguageService service = new(
            () => null,
            () => ["fr-FR"]);

        Assert.AreEqual(AppLanguage.Japanese, service.GetEffectiveLanguage());
    }
}
