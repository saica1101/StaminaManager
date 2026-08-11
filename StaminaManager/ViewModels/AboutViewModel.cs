using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Resources;
using System.Diagnostics;

namespace StaminaManager.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    private const string LaunchFailureResourceId = "AboutInfoBar.Message";
    private const string LaunchFailureFallback =
        "The link could not be opened in the default browser.";
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly IAppResourceService _appResourceService;

    public AboutViewModel(
        IAppVersionProvider versionProvider,
        IExternalUriLauncher uriLauncher)
        : this(
            versionProvider,
            uriLauncher,
            new AppResourceService())
    {
    }

    public AboutViewModel(
        IAppVersionProvider versionProvider,
        IExternalUriLauncher uriLauncher,
        IAppResourceService appResourceService)
    {
        ArgumentNullException.ThrowIfNull(versionProvider);
        ArgumentNullException.ThrowIfNull(uriLauncher);
        ArgumentNullException.ThrowIfNull(appResourceService);
        _uriLauncher = uriLauncher;
        _appResourceService = appResourceService;
        VersionText = versionProvider.GetVersion().DisplayVersion;
    }

    public string VersionText { get; }

    [ObservableProperty]
    public partial bool IsInfoBarOpen { get; private set; }

    [ObservableProperty]
    public partial string InfoBarMessage { get; private set; } = string.Empty;

    [RelayCommand]
    private Task OpenGitHubAsync() => LaunchAsync(AppMetadata.GitHubUri);

    [RelayCommand]
    private Task OpenReadmeAsync() => LaunchAsync(AppMetadata.ReadmeUri);

    public void DismissInfoBar() => IsInfoBarOpen = false;

    private async Task LaunchAsync(Uri uri)
    {
        try
        {
            if (await _uriLauncher.LaunchAsync(uri))
            {
                IsInfoBarOpen = false;
                return;
            }
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "External URI launch failed: "
                + exception.GetType().Name);
        }

        InfoBarMessage = ResolveLaunchFailureMessage();
        IsInfoBarOpen = true;
    }

    private string ResolveLaunchFailureMessage()
    {
        try
        {
            string message = _appResourceService.GetString(
                LaunchFailureResourceId);
            return string.IsNullOrWhiteSpace(message)
                || string.Equals(
                    message,
                    LaunchFailureResourceId,
                    StringComparison.Ordinal)
                ? LaunchFailureFallback
                : message;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "About resource resolution failed: "
                + exception.GetType().Name);
            return LaunchFailureFallback;
        }
    }

    private static bool IsProcessFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}
