using StaminaManager.Core.Abstractions;
using StaminaManager.Infrastructure.Notifications;
using Windows.UI.Notifications;

namespace StaminaManager.Tests.Notifications;

[TestClass]
public sealed class NotificationPermissionServiceTests
{
    [TestMethod]
    [DataRow(
        NotificationSetting.Enabled,
        NotificationPermissionState.Enabled,
        true)]
    [DataRow(
        NotificationSetting.DisabledForApplication,
        NotificationPermissionState.DisabledForApplication,
        false)]
    [DataRow(
        NotificationSetting.DisabledForUser,
        NotificationPermissionState.DisabledForUser,
        false)]
    [DataRow(
        NotificationSetting.DisabledByGroupPolicy,
        NotificationPermissionState.DisabledByPolicy,
        false)]
    [DataRow(
        NotificationSetting.DisabledByManifest,
        NotificationPermissionState.DisabledByManifest,
        false)]
    [DataRow(
        (NotificationSetting)int.MaxValue,
        NotificationPermissionState.Unsupported,
        false)]
    public async Task GetStatusAsync_MapsWindowsSetting(
        NotificationSetting platformSetting,
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
        NotificationSetting setting)
        : INotificationSettingsAdapter
    {
        public NotificationSetting GetSetting() => setting;
    }
}
