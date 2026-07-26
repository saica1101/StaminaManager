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
    private const string BackupBusyMessage =
        "バックアップ処理中です。完了してからもう一度お試しください。";
    private const string UnexpectedFailureMessage =
        "設定を変更できませんでした。もう一度お試しください。";
    private readonly GameManager _gameManager;
    private readonly IThemeService _themeService;
    private readonly IBackdropService _backdropService;
    private readonly IStartupService _startupService;
    private readonly INotificationReconciler _notificationReconciler;
    private readonly INotificationPermissionService
        _notificationPermissionService;
    private readonly ISettingsLauncher _settingsLauncher;
    private readonly AppCoordinator? _appCoordinator;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);

    public SettingsViewModel(
        GameManager gameManager,
        IThemeService themeService,
        IBackdropService backdropService)
        : this(
            gameManager,
            themeService,
            backdropService,
            new PassThroughStartupService(),
            new PassThroughNotificationReconciler(),
            new PassThroughPermissionService(),
            new PassThroughSettingsLauncher())
    {
    }

    public SettingsViewModel(
        GameManager gameManager,
        IThemeService themeService,
        IBackdropService backdropService,
        IStartupService startupService)
        : this(
            gameManager,
            themeService,
            backdropService,
            startupService,
            new PassThroughNotificationReconciler(),
            new PassThroughPermissionService(),
            new PassThroughSettingsLauncher())
    {
    }

    public SettingsViewModel(
        GameManager gameManager,
        IThemeService themeService,
        IBackdropService backdropService,
        IStartupService startupService,
        INotificationReconciler notificationReconciler,
        INotificationPermissionService notificationPermissionService,
        ISettingsLauncher settingsLauncher,
        AppCoordinator? appCoordinator = null)
    {
        ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(backdropService);
        ArgumentNullException.ThrowIfNull(startupService);
        ArgumentNullException.ThrowIfNull(notificationReconciler);
        ArgumentNullException.ThrowIfNull(notificationPermissionService);
        ArgumentNullException.ThrowIfNull(settingsLauncher);

        _gameManager = gameManager;
        _themeService = themeService;
        _backdropService = backdropService;
        _startupService = startupService;
        _notificationReconciler = notificationReconciler;
        _notificationPermissionService = notificationPermissionService;
        _settingsLauncher = settingsLauncher;
        _appCoordinator = appCoordinator;
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
    [NotifyPropertyChangedFor(nameof(IsSettingsInteractionEnabled))]
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
    [NotifyPropertyChangedFor(nameof(NotificationAvailabilityText))]
    public partial NotificationPermissionState WindowsNotificationState
    {
        get;
        private set;
    } = NotificationPermissionState.Enabled;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupStatusVisibility))]
    [NotifyPropertyChangedFor(nameof(IsSettingsInteractionEnabled))]
    public partial bool IsBackupBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupStatusVisibility))]
    public partial string BackupStatusText { get; private set; } =
        string.Empty;

    public bool IsDarkTheme => Theme == AppTheme.Dark;

    public bool IsReady =>
        InitializationState == SettingsInitializationState.Ready;

    public bool IsSettingsInteractionEnabled => IsReady && !IsBackupBusy;

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

    public Visibility BackupStatusVisibility =>
        IsBackupBusy || !string.IsNullOrEmpty(BackupStatusText)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string NotificationAvailabilityText =>
        WindowsNotificationState switch
        {
            NotificationPermissionState.Enabled =>
                "Windowsの通知は利用できます。",
            NotificationPermissionState.DisabledForApplication =>
                "Windowsのアプリごとの設定で通知が無効です。",
            NotificationPermissionState.DisabledForUser =>
                "Windows全体の通知が無効です。",
            NotificationPermissionState.DisabledByPolicy =>
                "組織のポリシーにより通知が無効です。",
            NotificationPermissionState.DisabledByManifest =>
                "アプリの通知構成が無効です。",
            _ => "この環境ではWindows通知を利用できません。",
        };

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

    public async Task<bool> SetStartupEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureReady())
        {
            return false;
        }

        await _mutationGate.WaitAsync(cancellationToken);
        try
        {
            bool previousEnabled =
                _gameManager.CurrentData.Settings.StartupEnabled;
            StartupChangeResult changeResult;
            try
            {
                changeResult = await _startupService.SetEnabledAsync(
                    isEnabled,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                Debug.WriteLine(
                    "StartupTask change failed: "
                    + exception.GetType().Name);
                ShowMessage(
                    "Windowsログイン時起動を変更できませんでした。",
                    InfoBarSeverity.Error);
                return false;
            }

            IsStartupEnabled = changeResult.Status.IsEnabled;
            if (!changeResult.IsApplied
                || changeResult.Status.IsEnabled != isEnabled)
            {
                ShowStartupFailure(changeResult.FailureReason);
                return false;
            }

            try
            {
                await _gameManager.UpdateSettingsAsync(
                    settings => settings with
                    {
                        StartupEnabled = isEnabled,
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await RollbackStartupAsync(previousEnabled);
                throw;
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                Debug.WriteLine(
                    "StartupTask setting save failed: "
                    + exception.GetType().Name);
                bool wasRestored = await RollbackStartupAsync(
                    previousEnabled);
                ShowMessage(
                    wasRestored
                        ? SaveFailureMessage
                        : SaveFailureMessage
                            + " Windowsの実際の状態は画面へ反映しました。",
                    InfoBarSeverity.Error);
                return false;
            }

            IsStartupEnabled = isEnabled;
            CloseInfoBar();
            return true;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    public Task<bool> SetNotificationsEnabledAsync(
        bool isEnabled,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureReady())
        {
            return Task.FromResult(false);
        }

        return PersistAndReconcileNotificationsAsync(
            settings => settings with
            {
                NotificationsEnabled = isEnabled,
            },
            () => AreNotificationsEnabled = isEnabled,
            cancellationToken);
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
        return PersistAndReconcileNotificationsAsync(
            settings => settings with
            {
                NotificationLeadMinutes = minutes,
            },
            () => NotificationLeadMinutes = minutes,
            cancellationToken);
    }

    public void SetWindowsNotificationAvailability(bool isAvailable)
    {
        AreWindowsNotificationsAvailable = isAvailable;
        WindowsNotificationState = isAvailable
            ? NotificationPermissionState.Enabled
            : NotificationPermissionState.DisabledForApplication;
    }

    public async Task RefreshNotificationAvailabilityAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            NotificationPermissionStatus status =
                await _notificationPermissionService.GetStatusAsync(
                    cancellationToken);
            WindowsNotificationState = status.State;
            AreWindowsNotificationsAvailable = status.IsAvailable;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Notification permission check failed: "
                + exception.GetType().Name);
            WindowsNotificationState = NotificationPermissionState.Unsupported;
            AreWindowsNotificationsAvailable = false;
            ShowMessage(
                "Windowsの通知状態を確認できませんでした。",
                InfoBarSeverity.Error);
        }
    }

    public async Task<bool> OpenWindowsNotificationSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            bool opened = await _settingsLauncher
                .OpenNotificationSettingsAsync(cancellationToken);
            if (!opened)
            {
                ShowMessage(
                    "Windowsの通知設定を開けませんでした。",
                    InfoBarSeverity.Error);
            }

            return opened;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Notification settings launch failed: "
                + exception.GetType().Name);
            ShowMessage(
                "Windowsの通知設定を開けませんでした。",
                InfoBarSeverity.Error);
            return false;
        }
    }

    public void PrepareBackupExport() =>
        ReportPreparation(SettingsPreparationAction.ExportBackup);

    public void PrepareBackupImport() =>
        ReportPreparation(SettingsPreparationAction.ImportBackup);

    public async Task ExportBackupAsync(
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        EnsureBackupIsIdle();
        AppCoordinator coordinator = GetBackupCoordinator();
        IsBackupBusy = true;
        BackupStatusText = "バックアップを作成しています…";
        try
        {
            await coordinator.ExportBackupAsync(
                destinationPath,
                cancellationToken);
            BackupStatusText = "バックアップを作成しました。";
            ShowMessage(
                "選択した場所にバックアップを保存しました。",
                InfoBarSeverity.Success,
                "バックアップが完了しました");
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    public async Task<BackupPreview> PreviewRestoreAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        EnsureBackupIsIdle();
        AppCoordinator coordinator = GetBackupCoordinator();
        IsBackupBusy = true;
        BackupStatusText = "バックアップの内容を確認しています…";
        try
        {
            return await coordinator.PreviewRestoreAsync(
                sourcePath,
                cancellationToken);
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    public async Task RestoreBackupAsync(
        string sourcePath,
        bool isReplacementConfirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureBackupIsIdle();
        AppCoordinator coordinator = GetBackupCoordinator();
        IsBackupBusy = true;
        BackupStatusText = "バックアップを復元しています…";
        try
        {
            _ = await coordinator.RestoreBackupAsync(
                sourcePath,
                isReplacementConfirmed,
                cancellationToken);
            AppSettings restored = _gameManager.CurrentData.Settings;
            Theme = restored.Theme;
            SelectedBackdrop = restored.Backdrop;
            ActualBackdrop = coordinator.LastBackdropResult?.ActualBackdrop
                ?? restored.Backdrop;
            CloseBehavior = restored.CloseBehavior;
            IsStartupEnabled = restored.StartupEnabled;
            AreNotificationsEnabled = restored.NotificationsEnabled;
            NotificationLeadMinutes = restored.NotificationLeadMinutes;

            bool hasRetry = coordinator.LastThemeResult?.IsApplied != true
                || coordinator.LastBackdropResult?.ErrorMessage is not null
                || !coordinator.IsStartupSynchronized
                || coordinator.LastNotificationReconcileResult?.HasFailures
                    == true;
            BackupStatusText = hasRetry
                ? "データを復元しました。Windows設定の再調整を次回も試行します。"
                : "バックアップを復元しました。";
            ShowMessage(
                BackupStatusText,
                hasRetry
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Success,
                hasRetry
                    ? "データは復元済みです"
                    : "復元が完了しました");
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

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
        BackdropResult? backdropResult = null,
        StartupStatus? startupStatus = null,
        bool isStartupSynchronized = true)
    {
        AppSettings settings = _gameManager.CurrentData.Settings;
        Theme = settings.Theme;
        SelectedBackdrop = settings.Backdrop;
        ActualBackdrop = backdropResult?.ActualBackdrop
            ?? settings.Backdrop;
        CloseBehavior = settings.CloseBehavior;
        IsStartupEnabled = startupStatus?.IsEnabled
            ?? settings.StartupEnabled;
        AreNotificationsEnabled = settings.NotificationsEnabled;
        NotificationLeadMinutes = settings.NotificationLeadMinutes;

        if (!isStartupSynchronized)
        {
            ShowMessage(
                startupStatus is null
                    ? "Windowsログイン時起動の実際の状態を確認できませんでした。"
                        + "Settingsを開き直して再試行してください。"
                    : "Windowsログイン時起動の実際の状態は画面へ反映しましたが、"
                        + "設定を保存できませんでした。再試行してください。",
                InfoBarSeverity.Error);
            return;
        }

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

    private async Task<bool> PersistAndReconcileNotificationsAsync(
        Func<AppSettings, AppSettings> update,
        Action publish,
        CancellationToken cancellationToken)
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
            catch (Exception exception) when (
                IsPersistenceFailure(exception))
            {
                ShowMessage(SaveFailureMessage, InfoBarSeverity.Error);
                return false;
            }

            publish();
            NotificationReconcileResult result;
            try
            {
                result = await _notificationReconciler.ReconcileAsync(
                    _gameManager.Games,
                    _gameManager.CurrentData.Settings,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                Debug.WriteLine(
                    "Notification reconciliation failed: "
                    + exception.GetType().Name);
                ShowNotificationReconcileFailure(
                    hasInvalidSchedule: false);
                return false;
            }

            await RefreshNotificationAvailabilityAsync(cancellationToken);
            if (result.HasFailures)
            {
                ShowNotificationReconcileFailure(result.HasInvalidSchedule);
                return false;
            }

            if (AreWindowsNotificationsAvailable)
            {
                CloseInfoBar();
            }

            return true;
        }
        finally
        {
            _mutationGate.Release();
        }
    }

    private void ShowNotificationReconcileFailure(bool hasInvalidSchedule)
    {
        ShowMessage(
            hasInvalidSchedule
                ? "通知時刻を計算できませんでした。"
                    + "通知する分数またはゲーム設定を見直してください。"
                : "設定は保存しましたが、一部のWindows通知を同期できませんでした。"
                    + "設定を変更して再試行してください。",
            InfoBarSeverity.Warning,
            "通知の同期が完了していません");
    }

    private void ReportPreparation(SettingsPreparationAction action)
    {
        PreparationRequested?.Invoke(action);
        ShowMessage(
            "この機能は準備中です。データやWindows設定は変更されていません。",
            InfoBarSeverity.Informational,
            "準備中の機能です");
    }

    private AppCoordinator GetBackupCoordinator() =>
        _appCoordinator ?? throw new InvalidOperationException(
            "バックアップ機能を利用できません。");

    private void EnsureBackupIsIdle()
    {
        if (IsBackupBusy)
        {
            throw new InvalidOperationException(
                "バックアップ処理は既に実行中です。");
        }
    }

    private async Task<bool> RollbackStartupAsync(bool previousEnabled)
    {
        try
        {
            StartupChangeResult rollback =
                await _startupService.SetEnabledAsync(
                    previousEnabled,
                    CancellationToken.None);
            IsStartupEnabled = rollback.Status.IsEnabled;
            return rollback.IsApplied
                && rollback.Status.IsEnabled == previousEnabled;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "StartupTask rollback failed: "
                + exception.GetType().Name);
            try
            {
                StartupStatus actual = await _startupService.GetStatusAsync(
                    CancellationToken.None);
                IsStartupEnabled = actual.IsEnabled;
            }
            catch (Exception statusException) when (
                !IsProcessFatal(statusException))
            {
                Debug.WriteLine(
                    "StartupTask status refresh failed: "
                    + statusException.GetType().Name);
            }

            return false;
        }
    }

    private void ShowStartupFailure(StartupFailureReason reason)
    {
        string message = reason switch
        {
            StartupFailureReason.DisabledByUser =>
                "ユーザーがWindowsのスタートアップ設定で無効にしています。"
                + "Windowsの設定から有効にしてください。",
            StartupFailureReason.DisabledByPolicy =>
                "組織のポリシーによりWindowsログイン時起動を有効にできません。",
            StartupFailureReason.EnabledByPolicy =>
                "組織のポリシーによりWindowsログイン時起動を無効にできません。",
            _ => "Windowsログイン時起動を変更できませんでした。",
        };
        ShowMessage(message, InfoBarSeverity.Warning);
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
        if (IsBackupBusy)
        {
            ShowMessage(
                BackupBusyMessage,
                InfoBarSeverity.Warning,
                "バックアップ処理中です");
            return false;
        }

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

    private sealed class PassThroughStartupService : IStartupService
    {
        public Task<StartupStatus> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new StartupStatus(StartupState.Disabled));

        public Task<StartupChangeResult> SetEnabledAsync(
            bool isEnabled,
            CancellationToken cancellationToken)
        {
            StartupStatus status = new(
                isEnabled ? StartupState.Enabled : StartupState.Disabled);
            return Task.FromResult(new StartupChangeResult(
                status,
                IsApplied: true,
                StartupFailureReason.None));
        }
    }

    private sealed class PassThroughNotificationReconciler
        : INotificationReconciler
    {
        public Task<NotificationReconcileResult> ReconcileAsync(
            IReadOnlyCollection<GameEntry> games,
            AppSettings settings,
            CancellationToken cancellationToken) => Task.FromResult(
                NotificationReconcileResult.Success);
    }

    private sealed class PassThroughPermissionService
        : INotificationPermissionService
    {
        public Task<NotificationPermissionStatus> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new NotificationPermissionStatus(
                    NotificationPermissionState.Enabled));
    }

    private sealed class PassThroughSettingsLauncher : ISettingsLauncher
    {
        public Task<bool> OpenNotificationSettingsAsync(
            CancellationToken cancellationToken) => Task.FromResult(true);
    }
}
