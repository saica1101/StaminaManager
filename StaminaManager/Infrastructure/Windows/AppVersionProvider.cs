using System.Diagnostics;
using System.Reflection;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using Windows.ApplicationModel;

namespace StaminaManager.Infrastructure.Windows;

public sealed class AppVersionProvider : IAppVersionProvider
{
    private readonly Func<AppVersionInfo?> _getPackageVersion;
    private readonly Func<string?> _getInformationalVersion;

    public AppVersionProvider()
        : this(GetPackageVersion, GetInformationalVersion)
    {
    }

    internal AppVersionProvider(
        Func<AppVersionInfo?> getPackageVersion,
        Func<string?> getInformationalVersion)
    {
        ArgumentNullException.ThrowIfNull(getPackageVersion);
        ArgumentNullException.ThrowIfNull(getInformationalVersion);
        _getPackageVersion = getPackageVersion;
        _getInformationalVersion = getInformationalVersion;
    }

    public AppVersionInfo GetVersion()
    {
        try
        {
            AppVersionInfo? packagedVersion = _getPackageVersion();
            if (packagedVersion is not null)
            {
                return packagedVersion;
            }
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Packaged version lookup failed: "
                + exception.GetType().Name);
        }

        try
        {
            if (TryParseInformationalVersion(
                _getInformationalVersion(),
                out AppVersionInfo? assemblyVersion))
            {
                return assemblyVersion!;
            }
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Assembly version lookup failed: "
                + exception.GetType().Name);
        }

        return AppVersionInfo.Fallback;
    }

    private static AppVersionInfo? GetPackageVersion()
    {
        PackageVersion version = Package.Current.Id.Version;
        return new AppVersionInfo(
            version.Major,
            version.Minor,
            version.Build,
            version.Revision);
    }

    private static string? GetInformationalVersion() =>
        Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

    private static bool TryParseInformationalVersion(
        string? value,
        out AppVersionInfo? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim();
        int metadataIndex = normalized.IndexOf('+');
        if (metadataIndex >= 0)
        {
            normalized = normalized[..metadataIndex];
        }

        int prereleaseIndex = normalized.IndexOf('-');
        if (prereleaseIndex >= 0)
        {
            normalized = normalized[..prereleaseIndex];
        }

        if (!Version.TryParse(normalized, out Version? parsed))
        {
            return false;
        }

        version = new AppVersionInfo(
            parsed.Major,
            parsed.Minor,
            parsed.Build < 0 ? 0 : parsed.Build,
            parsed.Revision < 0 ? 0 : parsed.Revision);
        return true;
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
