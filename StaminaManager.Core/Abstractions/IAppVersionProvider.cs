using StaminaManager.Core.Models;

namespace StaminaManager.Core.Abstractions;

public interface IAppVersionProvider
{
    AppVersionInfo GetVersion();
}
