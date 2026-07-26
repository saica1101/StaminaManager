using Microsoft.Windows.AppNotifications;
using StaminaManager.Core.Abstractions;
using StaminaManager.Infrastructure.Notifications;

namespace StaminaManager.Tests.Notifications;

[TestClass]
public sealed class NotificationPermissionServiceTests
{
    [TestMethod]
    [DataRow(
        AppNotificationSetting.Enabled,
        NotificationPermissionState.Enabled,
        true)]
    [DataRow(
        AppNotificationSetting.DisabledForApplication,
        NotificationPermissionState.DisabledForApplication,
        false)]
    [DataRow(
        AppNotificationSetting.DisabledForUser,
        NotificationPermissionState.DisabledForUser,
        false)]
    [DataRow(
        AppNotificationSetting.DisabledByGroupPolicy,
        NotificationPermissionState.DisabledByPolicy,
        false)]
    [DataRow(
        AppNotificationSetting.DisabledByManifest,
        NotificationPermissionState.DisabledByManifest,
        false)]
    [DataRow(
        AppNotificationSetting.Unsupported,
        NotificationPermissionState.Unsupported,
        false)]
    public async Task GetStatusAsync_MapsWindowsSetting(
        AppNotificationSetting platformSetting,
        NotificationPermissionState expectedState,
        bool expectedAvailable)
    {
        NotificationPermissionService service = new(
            new FakeNotificationSettingsAdapter(platformSetting));

        NotificationPermissionStatus status = await service.GetStatusAsync(
            CancellationToken.None);

        Assert.AreEqual(expectedState, status.State);
        Assert.AreEqual(expectedAvailable, status.IsAvailable);
    }

    private sealed class FakeNotificationSettingsAdapter(
        AppNotificationSetting setting)
        : INotificationSettingsAdapter
    {
        public AppNotificationSetting GetSetting() => setting;
    }
}
