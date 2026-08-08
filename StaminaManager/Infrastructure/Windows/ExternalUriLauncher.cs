using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;

namespace StaminaManager.Infrastructure.Windows;

public sealed class ExternalUriLauncher : IExternalUriLauncher
{
    private readonly Func<Uri, Task<bool>> _launchAsync;

    public ExternalUriLauncher()
        : this(LaunchWithWindowsAsync)
    {
    }

    internal ExternalUriLauncher(Func<Uri, Task<bool>> launchAsync)
    {
        ArgumentNullException.ThrowIfNull(launchAsync);
        _launchAsync = launchAsync;
    }

    public Task<bool> LaunchAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!AppMetadata.IsAllowedExternalUri(uri))
        {
            return Task.FromResult(false);
        }

        return _launchAsync(uri);
    }

    private static async Task<bool> LaunchWithWindowsAsync(Uri uri) =>
        await global::Windows.System.Launcher.LaunchUriAsync(uri);
}
