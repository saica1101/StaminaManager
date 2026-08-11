using System.Diagnostics;
using System.Globalization;
using Microsoft.Windows.ApplicationModel.Resources;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;
using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Infrastructure.Resources;

public sealed class AppResourceService : IAppResourceService
{
    private readonly Func<string, string> _getString;

    public AppResourceService()
        : this(GetEffectiveLanguageOrDefault())
    {
    }

    public AppResourceService(AppLanguage sessionLanguage)
    {
        Lazy<(
            ResourceManager Manager,
            ResourceMap Map,
            ResourceContext Context)> resourceScope = new(
            () => CreateResourceScope(sessionLanguage),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _getString = resourceId =>
        {
            (_, ResourceMap map, ResourceContext context) = resourceScope.Value;
            return map.GetValue(resourceId, context).ValueAsString;
        };
    }

    internal AppResourceService(Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(getString);
        _getString = getString;
    }

    private static (
        ResourceManager Manager,
        ResourceMap Map,
        ResourceContext Context) CreateResourceScope(
        AppLanguage sessionLanguage)
    {
        ResourceManager manager = new();
        ResourceContext context = manager.CreateResourceContext();
        context.QualifierValues[KnownResourceQualifierName.Language] =
            LanguagePolicy.GetLanguageTag(sessionLanguage);
        ResourceMap map = manager.MainResourceMap.GetSubtree("Resources");
        return (manager, map, context);
    }

    internal static AppLanguage GetEffectiveLanguageOrDefault()
    {
        try
        {
            return new AppLanguageService().GetEffectiveLanguage();
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Session language resolution failed: "
                + exception.GetType().Name);
            return AppLanguage.Japanese;
        }
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
