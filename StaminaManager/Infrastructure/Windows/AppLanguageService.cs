using Microsoft.Windows.Globalization;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;

namespace StaminaManager.Infrastructure.Windows;

public sealed class AppLanguageService : IAppLanguageService
{
    private readonly Func<string?> _getPrimaryLanguageOverride;
    private readonly Func<IReadOnlyList<string>> _getOsLanguages;

    public AppLanguageService()
        : this(
            () => ApplicationLanguages.PrimaryLanguageOverride,
            () => ApplicationLanguages.Languages)
    {
    }

    public AppLanguageService(
        Func<string?> getPrimaryLanguageOverride,
        Func<IReadOnlyList<string>> getOsLanguages)
    {
        ArgumentNullException.ThrowIfNull(getPrimaryLanguageOverride);
        ArgumentNullException.ThrowIfNull(getOsLanguages);
        _getPrimaryLanguageOverride = getPrimaryLanguageOverride;
        _getOsLanguages = getOsLanguages;
    }

    public AppLanguage GetEffectiveLanguage()
    {
        if (LanguagePolicy.TryGetLanguage(
            _getPrimaryLanguageOverride(),
            out AppLanguage overrideLanguage))
        {
            return overrideLanguage;
        }

        foreach (string languageTag in _getOsLanguages())
        {
            if (LanguagePolicy.TryGetLanguage(languageTag, out AppLanguage language))
            {
                return language;
            }
        }

        return AppLanguage.Japanese;
    }
}
