using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using System.Diagnostics;

namespace StaminaManager.ViewModels;

public enum SettingsPreparationAction
{
    ExportBackup,
    ImportBackup,
    OpenWindowsNotificationSettings,
}

public enum SettingsInitializationState
{
    Loading,
    Ready,
    Failed,
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private enum AppearanceRollbackStatus
    {
        Restored,
        SafeFallback,
        Failed,
        Unknown,
    }

    private const string SaveFailureMessage =
        "設定を保存できませんでした。以前の設定に戻しました。";
    private const string NotReadyMessage =
        "設定を読み込み中です。完了してからもう一度お試しください。";
    private const string InitializationFailureMessage =
        "設定を読み込めませんでした。アプリを再起動してください。";
    private const string UnexpectedFailureMessage =
        "設定を変更できませんでした。もう一度お試しください。";
    private readonly GameManager _gameManager;
    private readonly IThemeService _themeService;
    private readonly IBackdropService _backdropService;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public SettingsViewModel(
        GameManager gameManager,
        IThemeService themeService,
        IBackdropService backdropService)
    {
        ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(backdropService);

        _gameManager = gameManager;
        _themeService = themeService;
        _backdropService = backdropService;
        AppSettings settings = gameManager.CurrentData.Settings;
        Theme = settings.Theme;
        SelectedBackdrop = settings.Backdrop;
        ActualBackdrop = settings.Backdrop;
        CloseBehavior = settings.CloseBehavior;
        IsStartupEnabled = settings.StartupEnabled;
        AreNotificationsEnabled = settings.NotificationsEnabled;
        NotificationLeadMinutes = settings.NotificationLeadMinutes;
    }

    public event Action<SettingsPreparationAction>? PreparationRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    [NotifyPropertyChangedFor(nameof(IsLoading))]
    [NotifyPropertyChangedFor(nameof(IsFailed))]
    [NotifyPropertyChangedFor(nameof(LoadingVisibility))]
    [NotifyPropertyChangedFor(nameof(FailedVisibility))]
    public partial SettingsInitializationState InitializationState
    {
        get;
        private set;
    } = SettingsInitializationState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    public partial AppTheme Theme { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedBackdropIndex))]
    public partial BackdropKind SelectedBackdrop { get; private set; }

    [ObservableProperty]
    public partial BackdropKind ActualBackdrop { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CloseBehaviorIndex))]
    public partial CloseBehavior CloseBehavior { get; private set; }

    [ObservableProperty]
    public partial bool IsStartupEnabled { get; private set; }

    [ObservableProperty]
    public partial bool AreNotificationsEnabled { get; private set; }

    [ObservableProperty]
    public partial int NotificationLeadMinutes { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(IsOpenWindowsNotificationSettingsVisible))]
    [NotifyPropertyChangedFor(
        nameof(OpenWindowsNotificationSettingsVisibility))]
    [NotifyPropertyChangedFor(nameof(NotificationAvailabilityText))]
    public partial bool AreWindowsNotificationsAvailable
    {
        get;
        private set;
    } = true;

    [ObservableProperty]
    public partial bool IsInfoBarOpen { get; private set; }

    [ObservableProperty]
    public partial string InfoBarMessage { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string InfoBarTitle { get; private set; } =
        "設定を完了できませんでした";

    [ObservableProperty]
    public partial InfoBarSeverity InfoBarSeverity { get; private set; } =
        InfoBarSeverity.Error;

    public bool IsDarkTheme => Theme == AppTheme.Dark;

    public bool IsReady =>
        InitializationState == SettingsInitializationState.Ready;

    public bool IsLoading =>
        InitializationState == SettingsInitializationState.Loading;

    public bool IsFailed =>
        InitializationState == SettingsInitializationState.Failed;

    public Visibility LoadingVisibility => IsReady
        || IsFailed
            ? Visibility.Collapsed
            : Visibility.Visible;

    public Visibility FailedVisibility => IsFailed
        ? Visibility.Visible
        : Visibility.Collapsed;

    public int SelectedBackdropIndex => (int)SelectedBackdrop;

    public int CloseBehaviorIndex => (int)CloseBehavior;

    public bool IsOpenWindowsNotificationSettingsVisible =>
        !AreWindowsNotificationsAvailable;

    public Visibility OpenWindowsNotificationSettingsVisibility =>
        IsOpenWindowsNotificationSettingsVisible
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string NotificationAvailabilityText =>
        AreWindowsNotificationsAvailable
            ? "Windowsの通知は利用できます。"
            : "Windowsの通知が無効です。通知設定を確認してください。";

    public async Task<bool> SetThemeAsync(
        AppTheme requestedTheme,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureReady())
        {
            return false;
        }

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            AppTheme previousTheme =
                _gameManager.CurrentData.Settings.Theme;
            ThemeResult result = _themeService.Apply(requestedTheme);
            Theme = result.ActualTheme;
            if (!result.IsApplied)
            {
                ShowMessage(
                    result.ErrorMessage
                    ?? "テーマを適用できませんでした。",
                    InfoBarSeverity.Error);
                return false;
            }

            try
            {
                await _gameManager.UpdateSettingsAsync(
                        settings => settings with
                        {
                            Theme = requestedTheme,
                        },
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                AppearanceRollbackStatus rollbackStatus =
                    RollbackTheme(previousTheme);
                ShowThemeRollbackMessage(
                    rollbackStatus,
                    wasCanceled: true);
                throw;
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                AppearanceRollbackStatus rollbackStatus =
                    RollbackTheme(previousTheme);
                ShowThemeRollbackMessage(
                    rollbackStatus,
                    wasCanceled: false);
                return false;
            }

            Theme = requestedTheme;
            CloseInfoBar();
            return true;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public async Task<bool> SetBackdropAsync(
        BackdropKind requestedBackdrop,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureReady())
        {
            return false;
        }

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            BackdropKind previousBackdrop =
                _gameManager.CurrentData.Settings.Backdrop;
            BackdropResult result = _backdropService.Apply(
                requestedBackdrop);
            ActualBackdrop = result.ActualBackdrop;
            if (!result.IsRequestedBackdropApplied)
            {
                SelectedBackdrop = previousBackdrop;
                ShowMessage(
                    result.ErrorMessage
                    ?? "選択した背景を使用できないため、単色背景を使用します。",
                    InfoBarSeverity.Warning,
                    "背景を単色表示へ切り替えました");
                return false;
            }

            try
            {
                await _gameManager.UpdateSettingsAsync(
                        settings => settings with
                        {
                            Backdrop = requestedBackdrop,
                        },
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                AppearanceRollbackStatus rollbackStatus =
                    RollbackBackdrop(previousBackdrop);
                ShowBackdropRollbackMessage(
                    rollbackStatus,
                    wasCanceled: true);
                throw;
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                AppearanceRollbackStatus rollbackStatus =
                    RollbackBackdrop(previousBackdrop);
                ShowBackdropRollbackMessage(
                    rollbackStatus,
                    wasCanceled: false);
                return false;
            }

            SelectedBackdrop = requestedBackdrop;
            CloseInfoBar();
            return true;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public Task<bool> SetCloseBehaviorAsync(
        CloseBehavior closeBehavior,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureReady())
        {
            return Task.FromResult(false);
        }

        if (!Enum.IsDefined(closeBehavior))
        {
            ShowMessage(
                "閉じる操作の設定が正しくありません。",
                InfoBarSeverity.Error);
            return Task.FromResult(false);
        }

        return PersistAsync(
            settings => settings with { CloseBehavior = closeBehavior },
            () => CloseBehavior = closeBehavior,
            cancellationToken);
    }

    public Task<bool> SetStartupEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureReady())
        {
            return Task.FromResult(false);
        }

        return PersistAsync(
            settings => settings with { StartupEnabled = isEnabled },
            () => IsStartupEnabled = isEnabled,
            cancellationToken,
            "設定を保存しました。Windowsログイン時起動の適用は準備中です。");
    }

    public Task<bool> SetNotificationsEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureReady())
        {
            return Task.FromResult(false);
        }

        return PersistAsync(
            settings => settings with
            {
                NotificationsEnabled = isEnabled,
            },
            () => AreNotificationsEnabled = isEnabled,
            cancellationToken,
            "設定を保存しました。Windows通知との同期は準備中です。");
    }

    public Task<bool> SetNotificationLeadMinutesAsync(
        double value,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureReady())
        {
            return Task.FromResult(false);
        }

        if (double.IsNaN(value)
            || double.IsInfinity(value)
            || value != Math.Truncate(value)
            || value is < AppSettings.MinNotificationLeadMinutes
                or > AppSettings.MaxNotificationLeadMinutes)
        {
            ShowMessage(
                "通知時間は0～525,600分の整数で入力してください。",
                InfoBarSeverity.Error);
            return Task.FromResult(false);
        }

        int minutes = checked((int)value);
        return PersistAsync(
            settings => settings with
            {
                NotificationLeadMinutes = minutes,
            },
            () => NotificationLeadMinutes = minutes,
            cancellationToken,
            "設定を保存しました。Windows通知との同期は準備中です。");
    }

    public void SetWindowsNotificationAvailability(bool isAvailable)
    {
        AreWindowsNotificationsAvailable = isAvailable;
    }

    public void PrepareBackupExport() =>
        ReportPreparation(SettingsPreparationAction.ExportBackup);

    public void PrepareBackupImport() =>
        ReportPreparation(SettingsPreparationAction.ImportBackup);

    public void PrepareWindowsNotificationSettings() => ReportPreparation(
        SettingsPreparationAction.OpenWindowsNotificationSettings);

    public void DismissInfoBar() => CloseInfoBar();

    public void MarkReady()
    {
        if (!_gameManager.IsInitialized)
        {
            MarkFailed();
            return;
        }

        InitializationState = SettingsInitializationState.Ready;
    }

    public void MarkFailed()
    {
        InitializationState = SettingsInitializationState.Failed;
        CloseInfoBar();
    }

    internal void ReportUnexpectedFailure() => ShowMessage(
        UnexpectedFailureMessage,
        InfoBarSeverity.Error);

    public void SynchronizeFromCurrentSettings(
        ThemeResult? themeResult = null,
        BackdropResult? backdropResult = null)
    {
        AppSettings settings = _gameManager.CurrentData.Settings;
        Theme = settings.Theme;
        SelectedBackdrop = settings.Backdrop;
        ActualBackdrop = backdropResult?.ActualBackdrop
            ?? settings.Backdrop;
        CloseBehavior = settings.CloseBehavior;
        IsStartupEnabled = settings.StartupEnabled;
        AreNotificationsEnabled = settings.NotificationsEnabled;
        NotificationLeadMinutes = settings.NotificationLeadMinutes;

        if (backdropResult is { IsRequestedBackdropApplied: false })
        {
            ShowMessage(
                backdropResult.ErrorMessage
                ?? "保存済みの背景を使用できないため、単色背景を使用します。",
                InfoBarSeverity.Warning,
                "背景を単色表示へ切り替えました");
            return;
        }

        if (themeResult is { IsApplied: false })
        {
            ShowMessage(
                themeResult.ErrorMessage
                ?? "保存済みのテーマを適用できませんでした。",
                InfoBarSeverity.Error);
            return;
        }

        CloseInfoBar();
    }

    private async Task<bool> PersistAsync(
        Func<AppSettings, AppSettings> update,
        Action publish,
        CancellationToken cancellationToken,
        string? successMessage = null)
    {
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await _gameManager.UpdateSettingsAsync(
                        update,
                        cancellationToken);
            }
            catch (Exception exception) when (IsPersistenceFailure(exception))
            {
                ShowMessage(
                    SaveFailureMessage,
                    InfoBarSeverity.Error);
                return false;
            }

            publish();
            if (successMessage is null)
            {
                CloseInfoBar();
            }
            else
            {
                ShowMessage(
                    successMessage,
                    InfoBarSeverity.Informational,
                    "設定を保存しました");
            }

            return true;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private void ReportPreparation(SettingsPreparationAction action)
    {
        PreparationRequested?.Invoke(action);
        ShowMessage(
            "この機能は準備中です。データやWindows設定は変更されていません。",
            InfoBarSeverity.Informational,
            "準備中の機能です");
    }

    private static bool IsPersistenceFailure(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidDataException;

    private static bool IsProcessFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;

    private AppearanceRollbackStatus RollbackTheme(
        AppTheme previousTheme)
    {
        try
        {
            ThemeResult rollback = _themeService.Apply(previousTheme);
            Theme = rollback.ActualTheme;
            return rollback.IsApplied
                ? AppearanceRollbackStatus.Restored
                : AppearanceRollbackStatus.Failed;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Theme rollback failed: "
                + exception.GetType().Name);
            return AppearanceRollbackStatus.Unknown;
        }
    }

    private AppearanceRollbackStatus RollbackBackdrop(
        BackdropKind previousBackdrop)
    {
        SelectedBackdrop = previousBackdrop;
        try
        {
            BackdropResult rollback = _backdropService.Apply(
                previousBackdrop);
            ActualBackdrop = rollback.ActualBackdrop;
            if (rollback.IsRequestedBackdropApplied)
            {
                return AppearanceRollbackStatus.Restored;
            }

            return rollback.ActualBackdrop == BackdropKind.Solid
                ? AppearanceRollbackStatus.SafeFallback
                : AppearanceRollbackStatus.Failed;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Backdrop rollback failed: "
                + exception.GetType().Name);
            return ApplySafeBackdropFallback();
        }
    }

    private AppearanceRollbackStatus ApplySafeBackdropFallback()
    {
        try
        {
            BackdropResult fallback = _backdropService.Apply(
                BackdropKind.Solid);
            ActualBackdrop = fallback.ActualBackdrop;
            return fallback.ActualBackdrop == BackdropKind.Solid
                ? AppearanceRollbackStatus.SafeFallback
                : AppearanceRollbackStatus.Failed;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Backdrop safe fallback failed: "
                + exception.GetType().Name);
            return AppearanceRollbackStatus.Unknown;
        }
    }

    private void ShowThemeRollbackMessage(
        AppearanceRollbackStatus rollbackStatus,
        bool wasCanceled)
    {
        string message = GetRollbackMessage(wasCanceled);
        message += rollbackStatus switch
        {
            AppearanceRollbackStatus.Restored => string.Empty,
            AppearanceRollbackStatus.Unknown =>
                " テーマの実際の表示状態を確認できません。"
                + "アプリを再起動してください。",
            _ =>
                " テーマ表示を以前に戻せないため、"
                + "アプリを再起動してください。",
        };

        ShowMessage(message, InfoBarSeverity.Error);
    }

    private void ShowBackdropRollbackMessage(
        AppearanceRollbackStatus rollbackStatus,
        bool wasCanceled)
    {
        string message = GetRollbackMessage(wasCanceled);
        message += rollbackStatus switch
        {
            AppearanceRollbackStatus.Restored => string.Empty,
            AppearanceRollbackStatus.SafeFallback =>
                " 背景は安全な単色表示へ切り替わりました。",
            AppearanceRollbackStatus.Unknown =>
                " 背景の実際の表示状態を確認できません。"
                + "アプリを再起動してください。",
            _ =>
                " 背景表示を以前に戻せないため、"
                + "アプリを再起動してください。",
        };

        ShowMessage(message, InfoBarSeverity.Error);
    }

    private static string GetRollbackMessage(bool wasCanceled) =>
        wasCanceled
            ? "設定の保存がキャンセルされました。以前の設定に戻しました。"
            : SaveFailureMessage;

    private bool EnsureReady()
    {
        if (IsReady && _gameManager.IsInitialized)
        {
            return true;
        }

        if (IsFailed || InitializationState == SettingsInitializationState.Ready)
        {
            InitializationState = SettingsInitializationState.Failed;
            ShowMessage(
                InitializationFailureMessage,
                InfoBarSeverity.Error,
                "設定を読み込めませんでした");
        }
        else
        {
            ShowMessage(
                NotReadyMessage,
                InfoBarSeverity.Warning,
                "設定を変更できません");
        }

        return false;
    }

    private void ShowMessage(
        string message,
        InfoBarSeverity severity,
        string title = "設定を完了できませんでした")
    {
        InfoBarTitle = title;
        InfoBarMessage = message;
        InfoBarSeverity = severity;
        IsInfoBarOpen = true;
    }

    private void CloseInfoBar()
    {
        InfoBarMessage = string.Empty;
        IsInfoBarOpen = false;
    }
}
