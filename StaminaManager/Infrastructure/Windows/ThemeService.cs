using Microsoft.UI.Xaml;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StaminaManager.Infrastructure.Windows;

public interface IThemeTarget
{
    ElementTheme RequestedTheme { get; set; }
}

public sealed class FrameworkElementThemeTarget(
    Func<FrameworkElement?> rootAccessor) : IThemeTarget
{
    public ElementTheme RequestedTheme
    {
        get => GetRoot().RequestedTheme;
        set => GetRoot().RequestedTheme = value;
    }

    private FrameworkElement GetRoot() => rootAccessor()
        ?? throw new InvalidOperationException(
            "テーマの適用先がまだ作成されていません。");
}

public sealed class ThemeService : IThemeService
{
    private const string ApplyFailureMessage =
        "テーマを適用できませんでした。以前のテーマを使用します。";
    private readonly IThemeTarget _target;
    private readonly Func<ApplicationTheme> _windowsThemeAccessor;

    public ThemeService(
        IThemeTarget target,
        Func<ApplicationTheme> windowsThemeAccessor)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(windowsThemeAccessor);
        _target = target;
        _windowsThemeAccessor = windowsThemeAccessor;
    }

    public AppTheme ResolveInitialTheme() =>
        _windowsThemeAccessor() == ApplicationTheme.Dark
            ? AppTheme.Dark
            : AppTheme.Light;

    public ThemeResult Apply(AppTheme requestedTheme)
    {
        if (!Enum.IsDefined(requestedTheme))
        {
            return new ThemeResult(
                requestedTheme,
                ResolveCurrentTheme(),
                IsApplied: false,
                "テーマの設定値が正しくありません。");
        }

        AppTheme previousTheme = ResolveCurrentTheme();
        try
        {
            _target.RequestedTheme = ToElementTheme(requestedTheme);
            return new ThemeResult(
                requestedTheme,
                requestedTheme,
                IsApplied: true,
                ErrorMessage: null);
        }
        catch (Exception exception) when (IsExpectedApplyException(exception))
        {
            Debug.WriteLine(
                "Theme apply failed: " + exception.GetType().Name);
            TryRestore(previousTheme);
            return new ThemeResult(
                requestedTheme,
                previousTheme,
                IsApplied: false,
                ApplyFailureMessage);
        }
    }

    private AppTheme ResolveCurrentTheme()
    {
        try
        {
            return _target.RequestedTheme switch
            {
                ElementTheme.Dark => AppTheme.Dark,
                ElementTheme.Light => AppTheme.Light,
                _ => ResolveInitialTheme(),
            };
        }
        catch (Exception exception) when (IsExpectedApplyException(exception))
        {
            Debug.WriteLine(
                "Theme target read failed: " + exception.GetType().Name);
            return ResolveInitialTheme();
        }
    }

    private void TryRestore(AppTheme theme)
    {
        try
        {
            _target.RequestedTheme = ToElementTheme(theme);
        }
        catch (Exception exception) when (IsExpectedApplyException(exception))
        {
            Debug.WriteLine(
                "Theme rollback failed: " + exception.GetType().Name);
        }
    }

    private static ElementTheme ToElementTheme(AppTheme theme) =>
        theme == AppTheme.Dark
            ? ElementTheme.Dark
            : ElementTheme.Light;

    private static bool IsExpectedApplyException(Exception exception) =>
        exception is InvalidOperationException
            or ArgumentException
            or COMException;
}
