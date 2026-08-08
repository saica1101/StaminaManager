using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class AboutInfrastructureTests
{
    [TestMethod]
    public void GetVersion_PackageVersionWithoutRevision_UsesThreeParts()
    {
        AppVersionProvider provider = new(
            () => new AppVersionInfo(1, 0, 0, 0),
            () => "9.9.9.9");

        Assert.AreEqual("1.0.0", provider.GetVersion().DisplayVersion);
    }

    [TestMethod]
    public void GetVersion_PackageRevisionNonZero_UsesFourParts()
    {
        AppVersionProvider provider = new(
            () => new AppVersionInfo(1, 0, 0, 7),
            () => "9.9.9.9");

        Assert.AreEqual("1.0.0.7", provider.GetVersion().DisplayVersion);
    }

    [TestMethod]
    public void GetVersion_PackageFailure_UsesAssemblyInformationalVersion()
    {
        AppVersionProvider provider = new(
            () => throw new InvalidOperationException(),
            () => "2.3.4.5");

        Assert.AreEqual("2.3.4.5", provider.GetVersion().DisplayVersion);
    }

    [TestMethod]
    public void GetVersion_WhenAllSourcesFail_UsesFinalFallback()
    {
        AppVersionProvider provider = new(
            () => null,
            () => null);

        Assert.AreEqual("0.0.0", provider.GetVersion().DisplayVersion);
    }

    [TestMethod]
    public void DefaultVersionProvider_IsSafeWithoutPackageIdentity()
    {
        AppVersionInfo version = new AppVersionProvider().GetVersion();

        Assert.IsFalse(string.IsNullOrWhiteSpace(version.DisplayVersion));
    }

    [TestMethod]
    public void AppMetadata_UsesExactFixedUris()
    {
        Assert.AreEqual(
            "https://github.com/saica1101/StaminaManager",
            AppMetadata.GitHubUri.OriginalString);
        Assert.AreEqual(
            "https://github.com/saica1101/StaminaManager/blob/develop/README.md",
            AppMetadata.ReadmeUri.OriginalString);
    }

    [TestMethod]
    public async Task LaunchAsync_AllowsGitHubUri()
    {
        Uri? launchedUri = null;
        ExternalUriLauncher launcher = new(uri =>
        {
            launchedUri = uri;
            return Task.FromResult(true);
        });

        bool result = await launcher.LaunchAsync(AppMetadata.GitHubUri);

        Assert.IsTrue(result);
        Assert.AreEqual(AppMetadata.GitHubUri, launchedUri);
    }

    [TestMethod]
    public async Task LaunchAsync_AllowsReadmeUri()
    {
        Uri? launchedUri = null;
        ExternalUriLauncher launcher = new(uri =>
        {
            launchedUri = uri;
            return Task.FromResult(true);
        });

        bool result = await launcher.LaunchAsync(AppMetadata.ReadmeUri);

        Assert.IsTrue(result);
        Assert.AreEqual(AppMetadata.ReadmeUri, launchedUri);
    }

    [TestMethod]
    [DataRow("http://github.com/saica1101/StaminaManager")]
    [DataRow("file:///C:/Windows/notepad.exe")]
    [DataRow("https://github.com/saica1101/StaminaManager/")]
    [DataRow("https://example.com")]
    public async Task LaunchAsync_RejectsNonFixedUris(string value)
    {
        bool called = false;
        ExternalUriLauncher launcher = new(_ =>
        {
            called = true;
            return Task.FromResult(true);
        });

        bool result = await launcher.LaunchAsync(new Uri(value));

        Assert.IsFalse(result);
        Assert.IsFalse(called);
    }
}
