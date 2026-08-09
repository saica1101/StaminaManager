using System.Diagnostics;
using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using StaminaManager.Core.Abstractions;

namespace StaminaManager.Infrastructure.Resources;

public sealed class AppResourceService : IAppResourceService
{
    private readonly Func<string, string> _getString;

    public AppResourceService()
        : this(static resourceId => new ResourceLoader().GetString(resourceId))
    {
    }

    internal AppResourceService(Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(getString);
        _getString = getString;
    }

    public string GetString(string resourceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        try
        {
            string value = _getString(resourceId);
            return string.IsNullOrWhiteSpace(value)
                ? resourceId
                : value;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Application resource resolution failed: "
                + exception.GetType().Name);
            return resourceId;
        }
    }

    public string Format(string resourceId, params object?[] args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        string format = GetString(resourceId);

        try
        {
            return string.Format(
                CultureInfo.CurrentCulture,
                format,
                args ?? []);
        }
        catch (FormatException exception)
        {
            Debug.WriteLine(
                "Application resource formatting failed: "
                + exception.GetType().Name);
            return resourceId;
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
