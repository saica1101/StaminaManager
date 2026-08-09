namespace StaminaManager.Core.Abstractions;

public interface IAppResourceService
{
    string GetString(string resourceId);

    string Format(string resourceId, params object?[] args);
}
