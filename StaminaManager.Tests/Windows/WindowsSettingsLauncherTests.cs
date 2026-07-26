using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Windows;

[TestClass]
public sealed class WindowsSettingsLauncherTests
{
    [TestMethod]
    public async Task OpenNotificationSettingsAsync_UsesSystemNotificationsUri()
    {
        FakeSettingsPlatformAdapter adapter = new();
        WindowsSettingsLauncher launcher = new(adapter);

        bool launched = await launcher.OpenNotificationSettingsAsync(
            CancellationToken.None);

        Assert.IsTrue(launched);
        Assert.AreEqual(
            "ms-settings:notifications",
            adapter.LaunchedUri?.OriginalString);
    }

    private sealed class FakeSettingsPlatformAdapter
        : ISettingsPlatformAdapter
    {
        public Uri? LaunchedUri { get; private set; }

        public Task<bool> LaunchAsync(Uri uri)
        {
            LaunchedUri = uri;
            return Task.FromResult(true);
        }
    }
}
