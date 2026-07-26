using StaminaManager.Core.Abstractions;

namespace StaminaManager.Infrastructure.Windows;

public sealed class WindowsSettingsLauncher : ISettingsLauncher
{
    private static readonly Uri NotificationSettingsUri = new(
        "ms-settings:notifications");
    private readonly ISettingsPlatformAdapter _adapter;

    public WindowsSettingsLauncher()
        : this(new SettingsPlatformAdapter())
    {
    }

    internal WindowsSettingsLauncher(ISettingsPlatformAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        _adapter = adapter;
    }

    public async Task<bool> OpenNotificationSettingsAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await _adapter.LaunchAsync(NotificationSettingsUri)
            .ConfigureAwait(false);
    }
}

internal interface ISettingsPlatformAdapter
{
    Task<bool> LaunchAsync(Uri uri);
}

internal sealed class SettingsPlatformAdapter : ISettingsPlatformAdapter
{
    public async Task<bool> LaunchAsync(Uri uri) =>
        await global::Windows.System.Launcher.LaunchUriAsync(uri);
}
