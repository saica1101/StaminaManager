using StaminaManager.Core.Models;

namespace StaminaManager.Core.Validation;

public static class LanguagePolicy
{
    public const string JapaneseLanguageTag = "ja-JP";

    public const string EnglishLanguageTag = "en-US";

    public static bool TryGetLanguage(
        string? languageTag,
        out AppLanguage language)
    {
        if (string.Equals(
            languageTag,
            JapaneseLanguageTag,
            StringComparison.OrdinalIgnoreCase))
        {
            language = AppLanguage.Japanese;
            return true;
        }

        if (string.Equals(
            languageTag,
            EnglishLanguageTag,
            StringComparison.OrdinalIgnoreCase))
        {
            language = AppLanguage.English;
            return true;
        }

        language = default;
        return false;
    }

    public static string GetLanguageTag(AppLanguage language) => language switch
    {
        AppLanguage.Japanese => JapaneseLanguageTag,
        AppLanguage.English => EnglishLanguageTag,
        _ => throw new ArgumentOutOfRangeException(nameof(language)),
    };
}
