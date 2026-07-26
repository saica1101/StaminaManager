namespace StaminaManager.Core.Abstractions;

public enum NotificationPermissionState
{
    Enabled,
    DisabledForApplication,
    DisabledForUser,
    DisabledByPolicy,
    DisabledByManifest,
    Unsupported,
}

public sealed record NotificationPermissionStatus(
    NotificationPermissionState State)
{
    public bool IsAvailable => State == NotificationPermissionState.Enabled;
}

public interface INotificationPermissionService
{
    Task<NotificationPermissionStatus> GetStatusAsync(
        CancellationToken cancellationToken);
}
