namespace StaminaManager.Core.Abstractions;

public interface IExternalUriLauncher
{
    Task<bool> LaunchAsync(Uri uri);
}
