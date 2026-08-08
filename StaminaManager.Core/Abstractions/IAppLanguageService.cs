using StaminaManager.Core.Models;

namespace StaminaManager.Core.Abstractions;

public enum LanguageFailureReason
{
    None,
    Unsupported,
    PlatformError,
}

public enum LanguageConsistencyState
{
    Synchronized,
    Inconsistent,
}

public sealed record LanguageChangeResult(
    AppLanguage RequestedLanguage,
    bool IsApplied,
    LanguageFailureReason FailureReason);

public interface IAppLanguageService
{
    AppLanguage GetEffectiveLanguage();

    LanguageChangeResult SetLanguage(AppLanguage language);
}
