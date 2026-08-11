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
    private const string JapaneseLaunchFailureFallback =
        "リンクを既定のブラウザーで開けませんでした。";
    private const string EnglishLaunchFailureFallback =
        "The link could not be opened in the default browser.";
    private readonly IExternalUriLauncher _uriLauncher;
    private readonly IAppResourceService _appResourceService;
    private readonly AppLanguage _sessionLanguage;

    public AboutViewModel(
        IAppVersionProvider versionProvider,
        IExternalUriLauncher uriLauncher)
        : this(
            versionProvider,
            uriLauncher,
            CreateSessionResources())
    {
    }

    private AboutViewModel(
        IAppVersionProvider versionProvider,
        IExternalUriLauncher uriLauncher,
        (
            IAppResourceService ResourceService,
            AppLanguage SessionLanguage) session)
        : this(
            versionProvider,
            uriLauncher,
            session.ResourceService,
            session.SessionLanguage)
    {
    }

    public AboutViewModel(
        IAppVersionProvider versionProvider,
        IExternalUriLauncher uriLauncher,
        IAppResourceService appResourceService)
        : this(
            versionProvider,
            uriLauncher,
            appResourceService,
            AppResourceService.GetEffectiveLanguageOrDefault())
    {
    }

    public AboutViewModel(
        IAppVersionProvider versionProvider,
        IExternalUriLauncher uriLauncher,
        IAppResourceService appResourceService,
        AppLanguage sessionLanguage)
    {
        ArgumentNullException.ThrowIfNull(versionProvider);
        ArgumentNullException.ThrowIfNull(uriLauncher);
        ArgumentNullException.ThrowIfNull(appResourceService);
        _uriLauncher = uriLauncher;
        _appResourceService = appResourceService;
        _sessionLanguage = sessionLanguage;
        VersionText = versionProvider.GetVersion().DisplayVersion;
    }

    private static (
        IAppResourceService ResourceService,
        AppLanguage SessionLanguage) CreateSessionResources()
    {
        AppLanguage sessionLanguage =
            AppResourceService.GetEffectiveLanguageOrDefault();
        return (
            new AppResourceService(sessionLanguage),
            sessionLanguage);
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
                ? GetLaunchFailureFallback(_sessionLanguage)
                : message;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "About resource resolution failed: "
                + exception.GetType().Name);
            return GetLaunchFailureFallback(_sessionLanguage);
        }
    }

    private static string GetLaunchFailureFallback(AppLanguage language) =>
        language == AppLanguage.English
            ? EnglishLaunchFailureFallback
            : JapaneseLaunchFailureFallback;

    private static bool IsProcessFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}
