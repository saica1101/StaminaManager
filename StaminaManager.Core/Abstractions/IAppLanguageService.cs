using StaminaManager.Core.Models;

namespace StaminaManager.Core.Abstractions;

public interface IAppLanguageService
{
    AppLanguage GetEffectiveLanguage();
}
