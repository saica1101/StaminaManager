using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using System.Diagnostics;

namespace StaminaManager.ViewModels;

public sealed partial class AboutViewModel : ObservableObject
{
    private readonly IExternalUriLauncher _uriLauncher;

    public AboutViewModel(
        IAppVersionProvider versionProvider,
        IExternalUriLauncher uriLauncher)
    {
        ArgumentNullException.ThrowIfNull(versionProvider);
        ArgumentNullException.ThrowIfNull(uriLauncher);
        _uriLauncher = uriLauncher;
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

        InfoBarMessage = "リンクを既定のブラウザーで開けませんでした。";
        IsInfoBarOpen = true;
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
