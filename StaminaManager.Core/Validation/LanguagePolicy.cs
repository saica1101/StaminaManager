using StaminaManager.Core.Models;

namespace StaminaManager.Core.Validation;

public static class LanguagePolicy
{
    public const string JapaneseLanguageTag = "ja-JP";

    public const string EnglishLanguageTag = "en-US";

    private static readonly LanguageDefinition[] Definitions =
    [
        new(AppLanguage.Japanese, JapaneseLanguageTag, 0),
        new(AppLanguage.English, EnglishLanguageTag, 1),
    ];

    public static bool TryGetLanguage(
        string? languageTag,
        out AppLanguage language)
    {
        foreach (LanguageDefinition definition in Definitions)
        {
            if (string.Equals(
                languageTag,
                definition.Tag,
                StringComparison.OrdinalIgnoreCase))
            {
                language = definition.Language;
                return true;
            }
        }

        language = default;
        return false;
    }

    public static bool TryGetLanguageTag(
        AppLanguage language,
        out string languageTag)
    {
        foreach (LanguageDefinition definition in Definitions)
        {
            if (definition.Language == language)
            {
                languageTag = definition.Tag;
                return true;
            }
        }

        languageTag = string.Empty;
        return false;
    }

    public static string GetLanguageTag(AppLanguage language) =>
        TryGetLanguageTag(language, out string languageTag)
            ? languageTag
            : throw new ArgumentOutOfRangeException(nameof(language));

    public static bool TryFromSelectionIndex(
        int index,
        out AppLanguage language)
    {
        foreach (LanguageDefinition definition in Definitions)
        {
            if (definition.SelectionIndex == index)
            {
                language = definition.Language;
                return true;
            }
        }

        language = default;
        return false;
    }

    public static int ToSelectionIndex(AppLanguage language)
    {
        foreach (LanguageDefinition definition in Definitions)
        {
            if (definition.Language == language)
            {
                return definition.SelectionIndex;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(language), language, null);
    }

    private sealed record LanguageDefinition(
        AppLanguage Language,
        string Tag,
        int SelectionIndex);
}
