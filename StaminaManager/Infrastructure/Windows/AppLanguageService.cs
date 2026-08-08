using Microsoft.Windows.Globalization;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;
using Windows.System.UserProfile;
using System.Diagnostics;

namespace StaminaManager.Infrastructure.Windows;

public sealed class AppLanguageService : IAppLanguageService
{
    private readonly Func<string?> _getPrimaryLanguageOverride;
    private readonly Func<IReadOnlyList<string>> _getOsLanguages;
    private readonly Action<string> _setPrimaryLanguageOverride;

    public AppLanguageService()
        : this(
            () => ApplicationLanguages.PrimaryLanguageOverride,
            () => GlobalizationPreferences.Languages,
            languageTag => ApplicationLanguages.PrimaryLanguageOverride =
                languageTag)
    {
    }

    public AppLanguageService(
        Func<string?> getPrimaryLanguageOverride,
        Func<IReadOnlyList<string>> getOsLanguages)
        : this(
            getPrimaryLanguageOverride,
            getOsLanguages,
            languageTag => ApplicationLanguages.PrimaryLanguageOverride =
                languageTag)
    {
    }

    public AppLanguageService(
        Func<string?> getPrimaryLanguageOverride,
        Func<IReadOnlyList<string>> getOsLanguages,
        Action<string> setPrimaryLanguageOverride)
    {
        ArgumentNullException.ThrowIfNull(getPrimaryLanguageOverride);
        ArgumentNullException.ThrowIfNull(getOsLanguages);
        ArgumentNullException.ThrowIfNull(setPrimaryLanguageOverride);
        _getPrimaryLanguageOverride = getPrimaryLanguageOverride;
        _getOsLanguages = getOsLanguages;
        _setPrimaryLanguageOverride = setPrimaryLanguageOverride;
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

    public LanguageChangeResult SetLanguage(AppLanguage language)
    {
        if (!LanguagePolicy.TryGetLanguageTag(
            language,
            out string languageTag))
        {
            return new LanguageChangeResult(
                language,
                IsApplied: false,
                LanguageFailureReason.Unsupported);
        }

        try
        {
            _setPrimaryLanguageOverride(languageTag);
            return new LanguageChangeResult(
                language,
                IsApplied: true,
                LanguageFailureReason.None);
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Application language override failed: "
                + exception.GetType().Name);
            return new LanguageChangeResult(
                language,
                IsApplied: false,
                LanguageFailureReason.PlatformError);
        }
    }

    private static bool IsProcessFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}
