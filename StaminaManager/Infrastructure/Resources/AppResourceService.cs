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
    private readonly Action<AppLanguage>? _setLanguageQualifier;

    public AppResourceService()
        : this(GetEffectiveLanguageOrDefault())
    {
    }

    public AppResourceService(AppLanguage sessionLanguage)
    {
        AppLanguage currentLanguage = sessionLanguage;
        Lazy<(
            ResourceManager Manager,
            ResourceMap Map,
            ResourceContext Context)> resourceScope = new(
            () => CreateResourceScope(currentLanguage),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _getString = resourceId =>
        {
            (_, ResourceMap map, ResourceContext context) = resourceScope.Value;
            return map.GetValue(resourceId, context).ValueAsString;
        };
        _setLanguageQualifier = language =>
        {
            currentLanguage = language;
            if (resourceScope.IsValueCreated)
            {
                resourceScope.Value.Context.QualifierValues[
                    KnownResourceQualifierName.Language] =
                    LanguagePolicy.GetLanguageTag(language);
            }
        };
    }

    internal AppResourceService(Func<string, string> getString)
    {
        ArgumentNullException.ThrowIfNull(getString);
        _getString = getString;
    }

    internal void SetLanguageQualifier(AppLanguage language)
    {
        _setLanguageQualifier?.Invoke(language);
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

internal static class LateBoundResourceText
{
    internal static string? TryGet(
        string resourceId,
        string consumer,
        Func<string, string?>? resolve = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(consumer);

        try
        {
            string? value = resolve is null
                ? new ResourceLoader().GetString(resourceId)
                : resolve(resourceId);
            return string.IsNullOrWhiteSpace(value)
                || string.Equals(
                    value,
                    resourceId,
                    StringComparison.Ordinal)
                ? null
                : value;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                $"{consumer} resource resolution failed: "
                + exception.GetType().Name);
            return null;
        }
    }

    internal static string Resolve(
        string resourceId,
        string consumer,
        Func<string, string?>? resolve = null) =>
        TryGet(resourceId, consumer, resolve)
        ?? GetFallback(resourceId);

    internal static AppLanguage GetEffectiveLanguage() =>
        AppResourceService.GetEffectiveLanguageOrDefault();

    internal static string GetFallback(
        string resourceId,
        AppLanguage language) => resourceId switch
        {
            "StaminaNotificationDetailFormat"
                when language == AppLanguage.English
                => "Full recovery scheduled for: {0:g}",
            "StaminaNotificationDetailFormat"
                => "全回復予定: {0:g}",
            "TrayOpenText" when language == AppLanguage.English => "Open",
            "TrayOpenText" => "開く",
            "TrayExitText" when language == AppLanguage.English => "Exit",
            "TrayExitText" => "終了",
            "TrayOpenAutomationName"
                when language == AppLanguage.English
                => "Open Stamina Manager",
            "TrayOpenAutomationName" => "Stamina Managerを開く",
            "TrayExitAutomationName"
                when language == AppLanguage.English
                => "Exit Stamina Manager",
            "TrayExitAutomationName" => "Stamina Managerを終了する",
            _ => resourceId,
        };

    internal static string GetFallback(string resourceId) =>
        GetFallback(resourceId, GetEffectiveLanguage());

    private static bool IsProcessFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}
