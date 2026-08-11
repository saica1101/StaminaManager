using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Resources;
using StaminaManager.ViewModels;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class AboutViewModelTests
{
    [TestMethod]
    public async Task ConstructingAboutViewModel_DoesNotLaunchAnything()
    {
        RecordingLauncher launcher = new();
        AboutViewModel viewModel = new(
            new FixedVersionProvider(new AppVersionInfo(1, 2, 3, 0)),
            launcher);

        Assert.AreEqual("1.2.3", viewModel.VersionText);
        Assert.AreEqual(0, launcher.CallCount);
        Assert.IsFalse(viewModel.IsInfoBarOpen);

        await Task.CompletedTask;
    }

    [TestMethod]
    public async Task OpenGitHubCommand_UsesFixedGitHubUri()
    {
        RecordingLauncher launcher = new();
        AboutViewModel viewModel = new(
            new FixedVersionProvider(new AppVersionInfo(1, 2, 3, 4)),
            launcher);

        await viewModel.OpenGitHubCommand.ExecuteAsync(null);

        Assert.AreEqual(AppMetadata.GitHubUri, launcher.LastUri);
        Assert.IsFalse(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    public async Task OpenReadmeCommand_UsesFixedReadmeUri()
    {
        RecordingLauncher launcher = new();
        AboutViewModel viewModel = new(
            new FixedVersionProvider(new AppVersionInfo(1, 2, 3, 4)),
            launcher);

        await viewModel.OpenReadmeCommand.ExecuteAsync(null);

        Assert.AreEqual(AppMetadata.ReadmeUri, launcher.LastUri);
        Assert.IsFalse(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    public async Task OpenLink_WhenLauncherReturnsFalse_ShowsInfoBar()
    {
        RecordingLauncher launcher = new() { Result = false };
        AboutViewModel viewModel = new(
            new FixedVersionProvider(new AppVersionInfo(1, 2, 3, 0)),
            launcher,
            new AppResourceService(resourceId =>
                resourceId == "AboutInfoBar.Message"
                    ? "Could not open the link in the default browser."
                    : resourceId));

        await viewModel.OpenGitHubCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsInfoBarOpen);
        Assert.IsFalse(string.IsNullOrWhiteSpace(viewModel.InfoBarMessage));
    }

    [TestMethod]
    public async Task OpenLink_WhenLauncherThrows_ShowsInfoBarWithoutRethrowing()
    {
        RecordingLauncher launcher = new()
        {
            Exception = new InvalidOperationException("not for UI"),
        };
        AboutViewModel viewModel = new(
            new FixedVersionProvider(new AppVersionInfo(1, 2, 3, 0)),
            launcher,
            new AppResourceService(resourceId =>
                resourceId == "AboutInfoBar.Message"
                    ? "Could not open the link in the default browser."
                    : resourceId));

        await viewModel.OpenReadmeCommand.ExecuteAsync(null);

        Assert.IsTrue(viewModel.IsInfoBarOpen);
        Assert.AreEqual(
            "Could not open the link in the default browser.",
            viewModel.InfoBarMessage);
        Assert.DoesNotContain("not for UI", viewModel.InfoBarMessage);
    }

    [TestMethod]
    public async Task OpenLink_WhenLauncherFails_UsesSessionResourceText()
    {
        RecordingLauncher launcher = new() { Result = false };
        AboutViewModel viewModel = new(
            new FixedVersionProvider(new AppVersionInfo(1, 2, 3, 0)),
            launcher,
            new AppResourceService(resourceId =>
                resourceId == "AboutInfoBar.Message"
                    ? "リンクを既定のブラウザーで開けませんでした。"
                    : resourceId));

        await viewModel.OpenGitHubCommand.ExecuteAsync(null);

        Assert.AreEqual(
            "リンクを既定のブラウザーで開けませんでした。",
            viewModel.InfoBarMessage);
    }

    private sealed class FixedVersionProvider(AppVersionInfo version)
        : IAppVersionProvider
    {
        public AppVersionInfo GetVersion() => version;
    }

    private sealed class RecordingLauncher : IExternalUriLauncher
    {
        public Uri? LastUri { get; private set; }

        public int CallCount { get; private set; }

        public bool Result { get; init; } = true;

        public Exception? Exception { get; init; }

        public Task<bool> LaunchAsync(Uri uri)
        {
            LastUri = uri;
            CallCount++;
            if (Exception is not null)
            {
                throw Exception;
            }

            return Task.FromResult(Result);
        }
    }
}
