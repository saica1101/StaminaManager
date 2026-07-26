namespace StaminaManager.Core.Abstractions;

public interface ISettingsLauncher
{
    Task<bool> OpenNotificationSettingsAsync(
        CancellationToken cancellationToken);
}
