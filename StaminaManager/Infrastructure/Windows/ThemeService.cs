using Windows.UI;
using Windows.UI.ViewManagement;
using Microsoft.UI.Xaml;
using Microsoft.UI.Dispatching;
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
    private readonly Func<AppTheme> _systemThemeAccessor;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly UISettings? _uiSettings;

    private AppTheme _selectedTheme;
    private AppTheme _effectiveTheme;

    // 既存テストなどとの互換用
    public ThemeService(
        IThemeTarget target,
        Func<ApplicationTheme> windowsThemeAccessor)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(windowsThemeAccessor);

        _target = target;

        _systemThemeAccessor = () =>
            windowsThemeAccessor() == ApplicationTheme.Dark
                ? AppTheme.Dark
                : AppTheme.Light;

        _selectedTheme = _systemThemeAccessor();
        _effectiveTheme = _selectedTheme;
    }

    // 実アプリ用
    public ThemeService(
        IThemeTarget target,
        DispatcherQueue dispatcherQueue)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(dispatcherQueue);

        _target = target;
        _dispatcherQueue = dispatcherQueue;

        _uiSettings = new UISettings();

        _systemThemeAccessor = () =>
            ResolveSystemTheme(_uiSettings);

        _selectedTheme = _systemThemeAccessor();
        _effectiveTheme = _selectedTheme;

        _uiSettings.ColorValuesChanged += OnColorValuesChanged;
    }

    public AppTheme ResolveInitialTheme() =>
        _systemThemeAccessor();

    public ThemeResult Apply(AppTheme requestedTheme)
    {
        if (!Enum.IsDefined(requestedTheme))
        {
            return new ThemeResult(
                requestedTheme,
                _selectedTheme,
                IsApplied: false,
                "テーマの設定値が正しくありません。");
        }

        AppTheme previousSelectedTheme = _selectedTheme;
        AppTheme previousEffectiveTheme = _effectiveTheme;

        try
        {
            AppTheme effectiveTheme =
                ResolveEffectiveTheme(requestedTheme);

            _target.RequestedTheme =
                ToElementTheme(effectiveTheme);

            _selectedTheme = requestedTheme;
            _effectiveTheme = effectiveTheme;

            return new ThemeResult(
                requestedTheme,
                requestedTheme,
                IsApplied: true,
                ErrorMessage: null);
        }
        catch (Exception exception)
            when (IsExpectedApplyException(exception))
        {
            Debug.WriteLine(
                "Theme apply failed: "
                + exception.GetType().Name);

            TryRestore(
                previousSelectedTheme,
                previousEffectiveTheme);

            return new ThemeResult(
                requestedTheme,
                previousSelectedTheme,
                IsApplied: false,
                ApplyFailureMessage);
        }
    }

    private void OnColorValuesChanged(
        UISettings sender,
        object args)
    {
        if (_dispatcherQueue is null)
        {
            return;
        }

        if (!_dispatcherQueue.TryEnqueue(
            ApplySystemThemeIfNeeded))
        {
            Debug.WriteLine(
                "System theme update could not be queued.");
        }
    }

    private void ApplySystemThemeIfNeeded()
    {
        if (_selectedTheme != AppTheme.System)
        {
            return;
        }

        AppTheme effectiveTheme =
            _systemThemeAccessor();

        if (effectiveTheme == _effectiveTheme)
        {
            return;
        }

        try
        {
            _target.RequestedTheme =
                ToElementTheme(effectiveTheme);

            _effectiveTheme = effectiveTheme;
        }
        catch (Exception exception)
            when (IsExpectedApplyException(exception))
        {
            Debug.WriteLine(
                "System theme update failed: "
                + exception.GetType().Name);
        }
    }

    private AppTheme ResolveEffectiveTheme(
        AppTheme requestedTheme) =>
        requestedTheme == AppTheme.System
            ? _systemThemeAccessor()
            : requestedTheme;

    private static AppTheme ResolveSystemTheme(
        UISettings settings)
    {
        Color foreground =
            settings.GetColorValue(
                UIColorType.Foreground);

        bool isForegroundLight =
            ((5 * foreground.G)
             + (2 * foreground.R)
             + foreground.B)
            > (8 * 128);

        return isForegroundLight
            ? AppTheme.Dark
            : AppTheme.Light;
    }

    private void TryRestore(
        AppTheme selectedTheme,
        AppTheme effectiveTheme)
    {
        try
        {
            _target.RequestedTheme =
                ToElementTheme(effectiveTheme);

            _selectedTheme = selectedTheme;
            _effectiveTheme = effectiveTheme;
        }
        catch (Exception exception)
            when (IsExpectedApplyException(exception))
        {
            Debug.WriteLine(
                "Theme rollback failed: "
                + exception.GetType().Name);
        }
    }

    private static ElementTheme ToElementTheme(
        AppTheme theme) =>
        theme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => throw new ArgumentOutOfRangeException(
                nameof(theme)),
        };

    private static bool IsExpectedApplyException(
        Exception exception) =>
        exception is InvalidOperationException
            or ArgumentException
            or COMException;
}