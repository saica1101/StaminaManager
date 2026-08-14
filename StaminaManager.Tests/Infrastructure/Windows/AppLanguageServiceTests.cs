using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class AppLanguageServiceTests
{
    [TestMethod]
    public void SetLanguage_JapaneseAndEnglishUseThePolicyTags()
    {
        List<string> setTags = [];
        AppLanguageService service = new(
            () => null,
            () => [],
            setTags.Add);

        LanguageChangeResult japanese = service.SetLanguage(
            AppLanguage.Japanese);
        LanguageChangeResult english = service.SetLanguage(
            AppLanguage.English);

        Assert.IsTrue(japanese.IsApplied);
        Assert.IsTrue(english.IsApplied);
        CollectionAssert.AreEqual(
            new[] { "ja-JP", "en-US" },
            setTags);
    }

    [TestMethod]
    public void SetLanguage_UnsetOverrideIsSynchronizedEvenWhenOsFallbackMatches()
    {
        List<string> setTags = [];
        AppLanguageService service = new(
            () => null,
            () => ["ja-JP"],
            setTags.Add);

        Assert.AreEqual(AppLanguage.Japanese, service.GetEffectiveLanguage());
        LanguageChangeResult result = service.SetLanguage(
            AppLanguage.Japanese);

        Assert.IsTrue(result.IsApplied);
        CollectionAssert.AreEqual(new[] { "ja-JP" }, setTags);
    }

    [TestMethod]
    public void SetLanguage_UnsupportedOverrideIsSynchronizedEvenWhenOsFallbackMatches()
    {
        List<string> setTags = [];
        AppLanguageService service = new(
            () => "fr-FR",
            () => ["ja-JP"],
            setTags.Add);

        Assert.AreEqual(AppLanguage.Japanese, service.GetEffectiveLanguage());
        LanguageChangeResult result = service.SetLanguage(
            AppLanguage.Japanese);

        Assert.IsTrue(result.IsApplied);
        CollectionAssert.AreEqual(new[] { "ja-JP" }, setTags);
    }

    [TestMethod]
    public void SetLanguage_ExactRawOverrideIsTheOnlyPlatformNoOp()
    {
        int setterCallCount = 0;
        AppLanguageService service = new(
            () => "ja-JP",
            () => ["en-US"],
            _ => setterCallCount++);

        Assert.AreEqual(AppLanguage.Japanese, service.GetEffectiveLanguage());
        LanguageChangeResult result = service.SetLanguage(
            AppLanguage.Japanese);

        Assert.IsTrue(result.IsApplied);
        Assert.AreEqual(0, setterCallCount);
    }

    [TestMethod]
    public void SetLanguage_UndefinedEnumIsRejectedWithoutCallingSetter()
    {
        int setterCallCount = 0;
        AppLanguageService service = new(
            () => null,
            () => [],
            _ => setterCallCount++);

        LanguageChangeResult result = service.SetLanguage((AppLanguage)999);

        Assert.IsFalse(result.IsApplied);
        Assert.AreEqual(
            LanguageFailureReason.Unsupported,
            result.FailureReason);
        Assert.AreEqual(0, setterCallCount);
    }

    [TestMethod]
    public void SetLanguage_SetterExceptionBecomesTypedFailure()
    {
        AppLanguageService service = new(
            () => null,
            () => [],
            _ => throw new InvalidOperationException("private detail"));

        LanguageChangeResult result = service.SetLanguage(
            AppLanguage.English);

        Assert.IsFalse(result.IsApplied);
        Assert.AreEqual(
            LanguageFailureReason.PlatformError,
            result.FailureReason);
    }

    [TestMethod]
    public void GetEffectiveLanguage_対応Overrideを最優先する()
    {
        AppLanguageService service = new(
            () => "en-US",
            () => ["ja-JP"]);

        Assert.AreEqual(AppLanguage.English, service.GetEffectiveLanguage());
    }

    [TestMethod]
    public void GetEffectiveLanguage_OSの第一優先言語が日本語ならJapaneseを使う()
    {
        AppLanguageService service = new(
            () => null,
            () => ["ja-JP", "en-US"]);

        Assert.AreEqual(AppLanguage.Japanese, service.GetEffectiveLanguage());
    }

    [TestMethod]
    public void GetEffectiveLanguage_OSの第一優先言語が日本語以外ならEnglishを使う()
    {
        AppLanguageService service = new(
            () => "fr-FR",
            () => ["de-DE", "ja-JP", "en-US"]);

        Assert.AreEqual(AppLanguage.English, service.GetEffectiveLanguage());
    }

    [TestMethod]
    public void GetEffectiveLanguage_OS言語一覧が空ならEnglishへFallbackする()
    {
        AppLanguageService service = new(
            () => null,
            () => []);

        Assert.AreEqual(AppLanguage.English, service.GetEffectiveLanguage());
    }

    [TestMethod]
    public void 既定コンストラクター_ユーザープロファイルのOS言語一覧を使う()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "StaminaManager",
            "Infrastructure",
            "Windows",
            "AppLanguageService.cs"));

        StringAssert.Contains(source, "using Windows.System.UserProfile;");
        StringAssert.Contains(source, "GlobalizationPreferences.Languages");
        Assert.DoesNotContain("ApplicationLanguages.Languages", source);
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
