using Microsoft.Windows.AppNotifications;
using StaminaManager.Core.Abstractions;

namespace StaminaManager.Infrastructure.Notifications;

public sealed class NotificationPermissionService
    : INotificationPermissionService
{
    private readonly INotificationSettingsAdapter _adapter;

    public NotificationPermissionService()
        : this(new NotificationSettingsAdapter())
    {
    }

    internal NotificationPermissionService(
        INotificationSettingsAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _adapter = adapter;
    }

    public Task<NotificationPermissionStatus> GetStatusAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        NotificationPermissionState state = _adapter.GetSetting() switch
        {
            AppNotificationSetting.Enabled =>
                NotificationPermissionState.Enabled,
            AppNotificationSetting.DisabledForApplication =>
                NotificationPermissionState.DisabledForApplication,
            AppNotificationSetting.DisabledForUser =>
                NotificationPermissionState.DisabledForUser,
            AppNotificationSetting.DisabledByGroupPolicy =>
                NotificationPermissionState.DisabledByPolicy,
            AppNotificationSetting.DisabledByManifest =>
                NotificationPermissionState.DisabledByManifest,
            _ => NotificationPermissionState.Unsupported,
        };
        return Task.FromResult(new NotificationPermissionStatus(state));
    }
}

internal interface INotificationSettingsAdapter
{
    AppNotificationSetting GetSetting();
}

internal sealed class NotificationSettingsAdapter
    : INotificationSettingsAdapter
{
    public AppNotificationSetting GetSetting() =>
        AppNotificationManager.Default.Setting;
}
