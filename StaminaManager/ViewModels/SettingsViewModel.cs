using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;

namespace StaminaManager.ViewModels;

public enum SettingsPreparationAction
{
    ExportBackup,
    ImportBackup,
    OpenWindowsNotificationSettings,
}

public sealed partial class SettingsViewModel : ObservableObject
{
    private const string SaveFailureMessage =
        "設定を保存できませんでした。以前の設定に戻しました。";
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
        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            AppTheme previousTheme =
                _gameManager.CurrentData.Settings.Theme;
            ThemeResult result = _themeService.Apply(requestedTheme);
            if (!result.IsApplied)
            {
                Theme = previousTheme;
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
            catch (Exception exception) when (IsPersistenceFailure(exception))
            {
                ThemeResult rollback = _themeService.Apply(previousTheme);
                Theme = previousTheme;
                ShowMessage(rollback.IsApplied
                    ? SaveFailureMessage
                    : SaveFailureMessage
                        + " 表示も元に戻せないため、アプリを再起動してください。",
                    InfoBarSeverity.Error);
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
            catch (Exception exception) when (IsPersistenceFailure(exception))
            {
                BackdropResult rollback = _backdropService.Apply(
                    previousBackdrop);
                SelectedBackdrop = previousBackdrop;
                ActualBackdrop = rollback.ActualBackdrop;
                ShowMessage(rollback.IsRequestedBackdropApplied
                    ? SaveFailureMessage
                    : SaveFailureMessage
                        + " 背景は安全な単色表示へ切り替わりました。",
                    InfoBarSeverity.Error);
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
        CancellationToken cancellationToken = default) => PersistAsync(
            settings => settings with { StartupEnabled = isEnabled },
            () => IsStartupEnabled = isEnabled,
            cancellationToken,
            "設定を保存しました。Windowsログイン時起動の適用は準備中です。");

    public Task<bool> SetNotificationsEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default) => PersistAsync(
            settings => settings with
            {
                NotificationsEnabled = isEnabled,
            },
            () => AreNotificationsEnabled = isEnabled,
            cancellationToken,
            "設定を保存しました。Windows通知との同期は準備中です。");

    public Task<bool> SetNotificationLeadMinutesAsync(
        double value,
        CancellationToken cancellationToken = default)
    {
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
