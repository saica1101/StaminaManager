using StaminaManager.Core.Abstractions;
using Windows.ApplicationModel;
using Windows.UI.Notifications;

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
            NotificationSetting.Enabled =>
                NotificationPermissionState.Enabled,
            NotificationSetting.DisabledForApplication =>
                NotificationPermissionState.DisabledForApplication,
            NotificationSetting.DisabledForUser =>
                NotificationPermissionState.DisabledForUser,
            NotificationSetting.DisabledByGroupPolicy =>
                NotificationPermissionState.DisabledByPolicy,
            NotificationSetting.DisabledByManifest =>
                NotificationPermissionState.DisabledByManifest,
            _ => NotificationPermissionState.Unsupported,
        };
        return Task.FromResult(new NotificationPermissionStatus(state));
    }
}

internal interface INotificationSettingsAdapter
{
    NotificationSetting GetSetting();
}

internal sealed class NotificationSettingsAdapter
    : INotificationSettingsAdapter
{
    private readonly ToastNotifier _notifier =
        ToastNotificationManager.CreateToastNotifier(
            AppInfo.Current.AppUserModelId);

    public NotificationSetting GetSetting() => _notifier.Setting;
}
