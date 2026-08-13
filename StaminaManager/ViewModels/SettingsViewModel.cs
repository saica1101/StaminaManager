using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;
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

    private string SaveFailureMessage => _appResourceService.GetString(
        "SettingsSaveFailure");

    private string NotReadyMessage => _appResourceService.GetString(
        "SettingsNotReady");

    private string InitializationFailureMessage =>
        _appResourceService.GetString("SettingsInitializationFailure");

    private string BackupBusyMessage => _appResourceService.GetString(
        "SettingsBackupBusy");

    private string UnexpectedFailureMessage => _appResourceService.GetString(
        "SettingsUnexpectedFailure");

    private string LanguageSaveFailureMessage => _appResourceService.GetString(
        "SettingsLanguageSaveFailure");

    private string LanguageRestartMessage => _appResourceService.GetString(
        "SettingsLanguageRestartRequired");

    private string LanguageInconsistentMessage =>
        _appResourceService.GetString("SettingsLanguageInconsistent");
    private readonly GameManager _gameManager;
    private readonly IThemeService _themeService;
    private readonly IBackdropService _backdropService;
    private readonly IStartupService _startupService;
    private readonly INotificationReconciler _notificationReconciler;
    private readonly INotificationPermissionService
        _notificationPermissionService;
    private readonly ISettingsLauncher _settingsLauncher;
    private readonly IAppResourceService _appResourceService;
    private readonly IAppLanguageService _appLanguageService;
    private readonly Func<AppLanguage, Task<bool>>? _applyLanguageAsync;
    private readonly AppCoordinator? _appCoordinator;
    private AppLanguage _activeLanguage;
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private int _lastAppliedAcrylicTintOpacityPercent;
    private string? _backupStatusResourceId;

    public SettingsViewModel(
        GameManager gameManager,
        IThemeService themeService,
        IBackdropService backdropService,
        IAppResourceService appResourceService)
        : this(
            gameManager,
            themeService,
            backdropService,
            new PassThroughStartupService(),
            new PassThroughNotificationReconciler(),
            new PassThroughPermissionService(),
            new PassThroughSettingsLauncher(),
            appResourceService)
    {
    }

    public SettingsViewModel(
        GameManager gameManager,
        IThemeService themeService,
        IBackdropService backdropService,
        IStartupService startupService,
        IAppResourceService appResourceService)
        : this(
            gameManager,
            themeService,
            backdropService,
            startupService,
            new PassThroughNotificationReconciler(),
            new PassThroughPermissionService(),
            new PassThroughSettingsLauncher(),
            appResourceService)
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
        IAppResourceService appResourceService,
        AppCoordinator? appCoordinator = null,
        IAppLanguageService? appLanguageService = null,
        AppLanguage? sessionLanguage = null,
        Func<AppLanguage, Task<bool>>? applyLanguageAsync = null)
    {
        ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(themeService);
        ArgumentNullException.ThrowIfNull(backdropService);
        ArgumentNullException.ThrowIfNull(startupService);
        ArgumentNullException.ThrowIfNull(notificationReconciler);
        ArgumentNullException.ThrowIfNull(notificationPermissionService);
        ArgumentNullException.ThrowIfNull(settingsLauncher);
        ArgumentNullException.ThrowIfNull(appResourceService);

        _gameManager = gameManager;
        _themeService = themeService;
        _backdropService = backdropService;
        _startupService = startupService;
        _notificationReconciler = notificationReconciler;
        _notificationPermissionService = notificationPermissionService;
        _settingsLauncher = settingsLauncher;
        _appResourceService = appResourceService;
        _appCoordinator = appCoordinator;
        _appLanguageService = appLanguageService
            ?? new PassThroughLanguageService();
        _activeLanguage = sessionLanguage
            ?? _appLanguageService.GetEffectiveLanguage();
        _applyLanguageAsync = applyLanguageAsync;
        InfoBarTitle = _appResourceService.GetString("SettingsErrorTitle");
        AppSettings settings = gameManager.CurrentData.Settings;
        Theme = settings.Theme;
        Language = settings.Language;
        SelectedBackdrop = settings.Backdrop;
        ActualBackdrop = settings.Backdrop;
        AcrylicTintOpacityPercent = settings.AcrylicTintOpacityPercent;
        _lastAppliedAcrylicTintOpacityPercent =
            settings.AcrylicTintOpacityPercent;
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
    [NotifyPropertyChangedFor(nameof(IsAcrylicOpacityEnabled))]
    [NotifyPropertyChangedFor(nameof(AcrylicOpacityHelpText))]
    public partial SettingsInitializationState InitializationState
    {
        get;
        private set;
    } = SettingsInitializationState.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDarkTheme))]
    public partial AppTheme Theme { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedLanguageIndex))]
    [NotifyPropertyChangedFor(nameof(IsLanguageRestartRequired))]
    public partial AppLanguage Language { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedBackdropIndex))]
    [NotifyPropertyChangedFor(nameof(IsAcrylicOpacityEnabled))]
    [NotifyPropertyChangedFor(nameof(AcrylicOpacityHelpText))]
    public partial BackdropKind SelectedBackdrop { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAcrylicOpacityEnabled))]
    [NotifyPropertyChangedFor(nameof(AcrylicOpacityHelpText))]
    public partial BackdropKind ActualBackdrop { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AcrylicOpacityValueText))]
    [NotifyPropertyChangedFor(nameof(AcrylicOpacityValueAutomationName))]
    [NotifyPropertyChangedFor(nameof(AcrylicOpacityHelpText))]
    public partial int AcrylicTintOpacityPercent { get; private set; }

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
    public partial string InfoBarTitle { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity InfoBarSeverity { get; private set; } =
        InfoBarSeverity.Error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupStatusVisibility))]
    [NotifyPropertyChangedFor(nameof(IsSettingsInteractionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsAcrylicOpacityEnabled))]
    [NotifyPropertyChangedFor(nameof(AcrylicOpacityHelpText))]
    public partial bool IsBackupBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsInteractionEnabled))]
    [NotifyPropertyChangedFor(nameof(IsAcrylicOpacityEnabled))]
    [NotifyPropertyChangedFor(nameof(AcrylicOpacityHelpText))]
    public partial bool IsAppearanceBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSettingsInteractionEnabled))]
    public partial bool IsLanguageBusy { get; private set; }

    [ObservableProperty]
    public partial LanguageConsistencyState LanguageConsistencyState
    {
        get;
        private set;
    } = LanguageConsistencyState.Synchronized;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BackupStatusVisibility))]
    public partial string BackupStatusText { get; private set; } =
        string.Empty;

    public bool IsDarkTheme => Theme == AppTheme.Dark;

    public bool IsReady =>
        InitializationState == SettingsInitializationState.Ready;

    public bool IsSettingsInteractionEnabled =>
        IsReady && !IsBackupBusy && !IsAppearanceBusy && !IsLanguageBusy;

    public bool IsAcrylicOpacityEnabled =>
        IsReady
        && !IsBackupBusy
        && !IsAppearanceBusy
        && SelectedBackdrop == BackdropKind.Acrylic
        && ActualBackdrop == BackdropKind.Acrylic;

    public string AcrylicOpacityValueText =>
        $"{AcrylicTintOpacityPercent}%";

    public string AcrylicOpacityValueAutomationName =>
        _appResourceService.Format(
            "AcrylicOpacityAutomationNameFormat",
            AcrylicTintOpacityPercent);

    public string AcrylicOpacityHelpText => IsAcrylicOpacityEnabled
        ? _appResourceService.Format(
            "AcrylicOpacityHelpTextEnabledFormat",
            AcrylicTintOpacityPercent)
        : _appResourceService.Format(
            "AcrylicOpacityHelpTextDisabledFormat",
            AcrylicTintOpacityPercent);

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

    public int SelectedBackdropIndex =>
        BackdropPolicy.ToSelectionIndex(SelectedBackdrop);

    public int SelectedLanguageIndex =>
        LanguagePolicy.ToSelectionIndex(Language);

    public bool IsLanguageRestartRequired =>
        Language != _activeLanguage;

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
                _appResourceService.GetString(
                    "NotificationAvailabilityEnabled"),
            NotificationPermissionState.DisabledForApplication =>
                _appResourceService.GetString(
                    "NotificationAvailabilityDisabledForApplication"),
            NotificationPermissionState.DisabledForUser =>
                _appResourceService.GetString(
                    "NotificationAvailabilityDisabledForUser"),
            NotificationPermissionState.DisabledByPolicy =>
                _appResourceService.GetString(
                    "NotificationAvailabilityDisabledByPolicy"),
            NotificationPermissionState.DisabledByManifest =>
                _appResourceService.GetString(
                    "NotificationAvailabilityDisabledByManifest"),
            _ => _appResourceService.GetString(
                "NotificationAvailabilityUnsupported"),
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
        IsAppearanceBusy = true;
        try
        {
            AppTheme previousTheme =
                _gameManager.CurrentData.Settings.Theme;
            ThemeResult result = _themeService.Apply(requestedTheme);
            Theme = result.ActualTheme;
            if (!result.IsApplied)
            {
                ShowMessage(
                    _appResourceService.GetString(
                        "SettingsThemeApplyFailure"),
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
            IsAppearanceBusy = false;
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
        IsAppearanceBusy = true;
        try
        {
            BackdropKind previousBackdrop =
                _gameManager.CurrentData.Settings.Backdrop;
            int acrylicTintOpacityPercent = _gameManager.CurrentData
                .Settings.AcrylicTintOpacityPercent;
            BackdropResult result = _backdropService.Apply(
                new BackdropRequest(
                    requestedBackdrop,
                    acrylicTintOpacityPercent));
            ActualBackdrop = result.ActualBackdrop;
            if (!result.IsRequestedBackdropApplied)
            {
                SelectedBackdrop = previousBackdrop;
                ShowBackdropFallbackMessage(result);
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
            AcrylicTintOpacityPercent = acrylicTintOpacityPercent;
            if (requestedBackdrop == BackdropKind.Acrylic)
            {
                _lastAppliedAcrylicTintOpacityPercent =
                    result.ActualAcrylicTintOpacityPercent
                    ?? acrylicTintOpacityPercent;
            }
            CloseInfoBar();
            return true;
        }
        finally
        {
            IsAppearanceBusy = false;
            _mutationGate.Release();
        }
    }

    public async Task<bool> SetLanguageAsync(
        AppLanguage requestedLanguage,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureReady())
        {
            return false;
        }

        if (!LanguagePolicy.TryGetLanguageTag(
            requestedLanguage,
            out _))
        {
            ShowMessage(
                _appResourceService.GetString(
                    "SettingsLanguageUnsupported"),
                InfoBarSeverity.Error);
            return false;
        }

        await _mutationGate.WaitAsync(cancellationToken);
        IsLanguageBusy = true;
        try
        {
            AppLanguage previousLanguage = _gameManager.CurrentData
                .Settings.Language;
            try
            {
                await _gameManager.UpdateSettingsAsync(
                        settings => settings with
                        {
                            Language = requestedLanguage,
                        },
                        cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                Debug.WriteLine(
                    "Language setting save failed: "
                    + exception.GetType().Name);
                ShowMessage(
                    LanguageSaveFailureMessage,
                    InfoBarSeverity.Error);
                return false;
            }

            LanguageChangeResult changeResult;
            try
            {
                changeResult = _appLanguageService.SetLanguage(
                    requestedLanguage);
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                Debug.WriteLine(
                    "Language override failed: "
                    + exception.GetType().Name);
                changeResult = new LanguageChangeResult(
                    requestedLanguage,
                    IsApplied: false,
                    LanguageFailureReason.PlatformError);
            }

            if (!changeResult.IsApplied)
            {
                if (previousLanguage == requestedLanguage)
                {
                    Language = previousLanguage;
                    LanguageConsistencyState =
                        LanguageConsistencyState.Inconsistent;
                    ShowLanguageSynchronizationFailure(
                        changeResult.FailureReason);
                    return false;
                }

                bool wasRolledBack = await RollbackLanguageAsync(
                    previousLanguage);
                if (wasRolledBack)
                {
                    Language = previousLanguage;
                    LanguageConsistencyState =
                        LanguageConsistencyState.Synchronized;
                    ShowLanguageChangeFailure(
                        changeResult.FailureReason);
                }
                else
                {
                    Language = requestedLanguage;
                    LanguageConsistencyState =
                        LanguageConsistencyState.Inconsistent;
                    ShowMessage(
                        LanguageInconsistentMessage,
                        InfoBarSeverity.Warning,
                        _appResourceService.GetString(
                            "SettingsLanguageInconsistentTitle"));
                }

                return false;
            }

            Language = requestedLanguage;
            LanguageConsistencyState =
                LanguageConsistencyState.Synchronized;
            if (_applyLanguageAsync is not null)
            {
                bool isUiApplied = await ApplyLanguageUiAsync(
                    requestedLanguage);
                if (!isUiApplied)
                {
                    bool isOverrideRolledBack =
                        RollbackLanguageOverride(previousLanguage);
                    bool isSettingsRolledBack = await RollbackLanguageAsync(
                        previousLanguage);
                    if (isOverrideRolledBack && isSettingsRolledBack)
                    {
                        Language = previousLanguage;
                        LanguageConsistencyState =
                            LanguageConsistencyState.Synchronized;
                        ShowLanguageChangeFailure(
                            LanguageFailureReason.PlatformError);
                    }
                    else
                    {
                        Language = _gameManager.CurrentData.Settings.Language;
                        LanguageConsistencyState =
                            LanguageConsistencyState.Inconsistent;
                        ShowLanguageSynchronizationFailure(
                            LanguageFailureReason.PlatformError);
                    }

                    return false;
                }

                _activeLanguage = requestedLanguage;
                OnPropertyChanged(nameof(IsLanguageRestartRequired));
            }

            NotificationReconcileResult notificationResult;
            try
            {
                notificationResult = await _notificationReconciler
                    .ReconcileAsync(
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
                    "Notification reconciliation failed after language change: "
                    + exception.GetType().Name);
                ShowNotificationReconcileFailure(
                    hasInvalidSchedule: false);
                return false;
            }

            if (notificationResult.HasFailures)
            {
                ShowNotificationReconcileFailure(
                    notificationResult.HasInvalidSchedule);
                return false;
            }

            if (_applyLanguageAsync is null && IsLanguageRestartRequired)
            {
                ShowMessage(
                    LanguageRestartMessage,
                    InfoBarSeverity.Informational,
                    _appResourceService.GetString(
                        "SettingsRestartRequiredTitle"));
            }
            else
            {
                CloseInfoBar();
            }

            return true;
        }
        finally
        {
            IsLanguageBusy = false;
            _mutationGate.Release();
        }
    }

    public bool PreviewAcrylicTintOpacity(int percent)
    {
        if (!EnsureReady())
        {
            return false;
        }

        if (!AcrylicOpacityPolicy.IsValid(percent))
        {
            ShowMessage(
                _appResourceService.GetString(
                    "SettingsAcrylicOpacityInvalid"),
                InfoBarSeverity.Error);
            return false;
        }

        if (!IsAcrylicOpacityEnabled)
        {
            return false;
        }

        int lastApplied = _lastAppliedAcrylicTintOpacityPercent;
        BackdropResult? result = null;
        try
        {
            result = _backdropService.Apply(
                new BackdropRequest(BackdropKind.Acrylic, percent));
            ActualBackdrop = result.ActualBackdrop;
            if (result.IsRequestedBackdropApplied)
            {
                AcrylicTintOpacityPercent =
                    result.ActualAcrylicTintOpacityPercent ?? percent;
                _lastAppliedAcrylicTintOpacityPercent =
                    AcrylicTintOpacityPercent;
                CloseInfoBar();
                return true;
            }
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Acrylic opacity preview failed: "
                + exception.GetType().Name);
        }

        AcrylicTintOpacityPercent = lastApplied;
        AppearanceRollbackStatus rollbackStatus =
            RollbackAcrylicOpacity(lastApplied);
        ShowAcrylicOpacityFailure(rollbackStatus);
        return false;
    }

    public Task<bool> PreviewAcrylicTintOpacityAsync(
        int percent,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(PreviewAcrylicTintOpacity(percent));
    }

    public async Task<bool> CommitAcrylicTintOpacityAsync(
        int percent,
        CancellationToken cancellationToken = default)
    {
        if (!EnsureReady())
        {
            return false;
        }

        if (!AcrylicOpacityPolicy.IsValid(percent))
        {
            ShowMessage(
                _appResourceService.GetString(
                    "SettingsAcrylicOpacityInvalid"),
                InfoBarSeverity.Error);
            return false;
        }

        await _mutationGate.WaitAsync(cancellationToken);
        if (!IsAcrylicOpacityEnabled)
        {
            _mutationGate.Release();
            return false;
        }

        try
        {
            int previousSaved = _gameManager.CurrentData.Settings
                .AcrylicTintOpacityPercent;
            if (_lastAppliedAcrylicTintOpacityPercent != percent)
            {
                BackdropResult result;
                try
                {
                    result = _backdropService.Apply(
                        new BackdropRequest(BackdropKind.Acrylic, percent));
                }
                catch (Exception exception) when (
                    !IsProcessFatal(exception))
                {
                    Debug.WriteLine(
                        "Acrylic opacity commit apply failed: "
                        + exception.GetType().Name);
                    AcrylicTintOpacityPercent = previousSaved;
                    AppearanceRollbackStatus rollbackStatus =
                        RollbackAcrylicOpacity(previousSaved);
                    ShowAcrylicOpacityFailure(rollbackStatus);
                    return false;
                }

                ActualBackdrop = result.ActualBackdrop;
                if (!result.IsRequestedBackdropApplied)
                {
                    AcrylicTintOpacityPercent = previousSaved;
                    AppearanceRollbackStatus rollbackStatus =
                        RollbackAcrylicOpacity(previousSaved);
                    ShowAcrylicOpacityFailure(rollbackStatus);
                    return false;
                }

                _lastAppliedAcrylicTintOpacityPercent =
                    result.ActualAcrylicTintOpacityPercent ?? percent;
            }

            try
            {
                await _gameManager.UpdateSettingsAsync(
                    settings => settings with
                    {
                        AcrylicTintOpacityPercent = percent,
                    },
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                AcrylicTintOpacityPercent = previousSaved;
                AppearanceRollbackStatus rollbackStatus =
                    RollbackAcrylicOpacity(previousSaved);
                ShowAcrylicOpacityRollbackMessage(
                    rollbackStatus,
                    wasCanceled: true);
                throw;
            }
            catch (Exception exception) when (!IsProcessFatal(exception))
            {
                Debug.WriteLine(
                    "Acrylic opacity setting save failed: "
                    + exception.GetType().Name);
                AcrylicTintOpacityPercent = previousSaved;
                AppearanceRollbackStatus rollbackStatus =
                    RollbackAcrylicOpacity(previousSaved);
                ShowAcrylicOpacityRollbackMessage(
                    rollbackStatus,
                    wasCanceled: false);
                return false;
            }

            AcrylicTintOpacityPercent = percent;
            _lastAppliedAcrylicTintOpacityPercent = percent;
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
                _appResourceService.GetString(
                    "SettingsCloseBehaviorInvalid"),
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
                    _appResourceService.GetString(
                        "SettingsStartupChangeFailure"),
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
                        : _appResourceService.GetString(
                            "SettingsStartupSaveFailureActualState"),
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
                _appResourceService.GetString(
                    "SettingsNotificationLeadInvalid"),
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
            if (!status.IsAvailable)
            {
                ShowNotificationPermissionMessage(status.State);
            }
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
                _appResourceService.GetString(
                    "SettingsNotificationAvailabilityCheckFailure"),
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
                    _appResourceService.GetString(
                        "SettingsNotificationSettingsLaunchFailure"),
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
                _appResourceService.GetString(
                    "SettingsNotificationSettingsLaunchFailure"),
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
        SetBackupStatus("SettingsBackupExportBusy");
        try
        {
            await coordinator.ExportBackupAsync(
                destinationPath,
                cancellationToken);
            SetBackupStatus("SettingsBackupExportSuccessStatus");
            ShowMessage(
                _appResourceService.GetString(
                    "SettingsBackupExportSuccessMessage"),
                InfoBarSeverity.Success,
                _appResourceService.GetString(
                    "SettingsBackupExportSuccessTitle"));
        }
        catch (OperationCanceledException)
        {
            SetBackupStatus(null);
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Backup export failed: "
                + exception.GetType().Name);
            SetBackupStatus("SettingsBackupExportFailureStatus");
            ShowMessage(
                _appResourceService.GetString(
                    "SettingsBackupExportFailureMessage"),
                InfoBarSeverity.Error,
                _appResourceService.GetString(
                    "SettingsBackupExportFailureTitle"));
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    public async Task<PreparedBackupRestore> PreviewRestoreAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        EnsureBackupIsIdle();
        AppCoordinator coordinator = GetBackupCoordinator();
        IsBackupBusy = true;
        SetBackupStatus("SettingsBackupPreviewBusy");
        try
        {
            return await coordinator.PreviewRestoreAsync(
                sourcePath,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            SetBackupStatus(null);
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Backup restore preview failed: "
                + exception.GetType().Name);
            SetBackupStatus("SettingsBackupImportFailureStatus");
            ShowMessage(
                _appResourceService.GetString(
                    "SettingsBackupImportFailureMessage"),
                InfoBarSeverity.Error,
                _appResourceService.GetString(
                    "SettingsBackupImportFailureTitle"));
            throw;
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    public async Task CancelPreparedRestoreAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        EnsureBackupIsIdle();
        AppCoordinator coordinator = GetBackupCoordinator();
        IsBackupBusy = true;
        SetBackupStatus("SettingsBackupCancelBusy");
        try
        {
            await coordinator.CancelPreparedRestoreAsync(
                sessionId,
                cancellationToken);
            SetBackupStatus(null);
        }
        catch (OperationCanceledException)
        {
            SetBackupStatus(null);
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Prepared backup restore cancellation failed: "
                + exception.GetType().Name);
            ReportBackupCancelFailure();
            throw;
        }
        finally
        {
            IsBackupBusy = false;
        }
    }

    public async Task<BackupRestoreResult> RestoreBackupAsync(
        string sessionId,
        bool isReplacementConfirmed,
        CancellationToken cancellationToken = default)
    {
        EnsureBackupIsIdle();
        AppCoordinator coordinator = GetBackupCoordinator();
        IsBackupBusy = true;
        SetBackupStatus("SettingsBackupRestoreBusy");
        AppLanguage previousActiveLanguage = _activeLanguage;
        bool isLanguageUiApplyFailed = false;
        bool isLanguageRollbackNotificationFailed = false;
        try
        {
            BackupRestoreResult result = await coordinator.RestoreBackupAsync(
                sessionId,
                isReplacementConfirmed,
                cancellationToken);
            SynchronizeFromCurrentData(coordinator);
            if (_applyLanguageAsync is not null
                && Language != previousActiveLanguage
                && coordinator.IsLanguageSynchronized)
            {
                if (await ApplyLanguageUiAsync(Language))
                {
                    MarkLiveLanguageApplied(Language);
                }
                else
                {
                    bool isOverrideRolledBack =
                        RollbackLanguageOverride(previousActiveLanguage);
                    bool isSettingsRolledBack = await RollbackLanguageAsync(
                        previousActiveLanguage);
                    if (isOverrideRolledBack && isSettingsRolledBack)
                    {
                        SynchronizeFromCurrentData(coordinator);
                        MarkLiveLanguageApplied(previousActiveLanguage);
                        isLanguageRollbackNotificationFailed =
                            await ReconcileNotificationsAfterLanguageRollbackAsync(
                                cancellationToken);
                    }
                    else
                    {
                        isLanguageUiApplyFailed = true;
                        MarkLanguageUiApplyFailed();
                    }
                }
            }

            bool hasRetry = result.IsPartial
                || result.RequiresDerivedStateRetry
                || coordinator.LastThemeResult?.IsApplied != true
                || coordinator.LastBackdropResult?.ErrorMessage is not null
                || !coordinator.IsStartupSynchronized
                || !coordinator.IsLanguageSynchronized
                || isLanguageUiApplyFailed
                || isLanguageRollbackNotificationFailed
                || coordinator.LastNotificationReconcileResult?.HasFailures
                    == true;
            bool isLanguageRestartRequired = IsLanguageRestartRequired;
            string backupStatusResourceId = hasRetry
                ? isLanguageRestartRequired
                    ? "SettingsBackupRestorePartialRestartStatus"
                    : "SettingsBackupRestorePartialStatus"
                : isLanguageRestartRequired
                    ? "SettingsBackupRestoreSuccessRestartStatus"
                    : "SettingsBackupRestoreSuccessStatus";
            SetBackupStatus(backupStatusResourceId);
            ShowMessage(
                BackupStatusText,
                hasRetry
                    ? InfoBarSeverity.Warning
                    : InfoBarSeverity.Success,
                hasRetry
                    ? _appResourceService.GetString(
                        "SettingsBackupRestorePartialTitle")
                    : _appResourceService.GetString(
                        "SettingsBackupRestoreSuccessTitle"));
            return result;
        }
        catch (OperationCanceledException)
        {
            SetBackupStatus(null);
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Backup restore failed: "
                + exception.GetType().Name);
            SetBackupStatus("SettingsBackupRestoreFailureStatus");
            ShowMessage(
                _appResourceService.GetString(
                    "SettingsBackupRestoreFailureMessage"),
                InfoBarSeverity.Error,
                _appResourceService.GetString(
                    "SettingsBackupRestoreFailureTitle"));
            throw;
        }
        finally
        {
            SynchronizeFromCurrentData(coordinator);
            if (isLanguageUiApplyFailed)
            {
                MarkLanguageUiApplyFailed();
            }
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

    internal void MarkLiveLanguageApplied(AppLanguage language)
    {
        _activeLanguage = language;
        Language = language;
        LanguageConsistencyState = LanguageConsistencyState.Synchronized;
        OnPropertyChanged(nameof(IsLanguageRestartRequired));
        RefreshLocalizedText();
    }

    internal void RefreshLocalizedText()
    {
        InfoBarTitle = _appResourceService.GetString("SettingsErrorTitle");
        if (_backupStatusResourceId is not null)
        {
            SetBackupStatus(_backupStatusResourceId);
        }
        OnPropertyChanged(nameof(AcrylicOpacityValueAutomationName));
        OnPropertyChanged(nameof(AcrylicOpacityHelpText));
        OnPropertyChanged(nameof(NotificationAvailabilityText));
    }

    internal void MarkLanguageUiApplyFailed()
    {
        LanguageConsistencyState = LanguageConsistencyState.Inconsistent;
        ShowLanguageSynchronizationFailure(
            LanguageFailureReason.PlatformError);
    }

    internal void ReportUnexpectedFailure() => ShowMessage(
        UnexpectedFailureMessage,
        InfoBarSeverity.Error);

    internal void ReportBackupImportFailure()
    {
        SetBackupStatus("SettingsBackupImportFailureStatus");
        ShowMessage(
            _appResourceService.GetString(
                "SettingsBackupImportFailureMessage"),
            InfoBarSeverity.Error,
            _appResourceService.GetString(
                "SettingsBackupImportFailureTitle"));
    }

    internal void ReportBackupCancelFailure()
    {
        SetBackupStatus("SettingsBackupCancelFailureStatus");
        ShowMessage(
            _appResourceService.GetString(
                "SettingsBackupCancelFailureMessage"),
            InfoBarSeverity.Error,
            _appResourceService.GetString(
                "SettingsBackupCancelFailureTitle"));
    }

    internal void ReportBackupRestoreFailure()
    {
        SetBackupStatus("SettingsBackupRestoreFailureStatus");
        ShowMessage(
            _appResourceService.GetString(
                "SettingsBackupRestoreFailureMessage"),
            InfoBarSeverity.Error,
            _appResourceService.GetString(
                "SettingsBackupRestoreFailureTitle"));
    }

    private async Task<bool> ApplyLanguageUiAsync(AppLanguage language)
    {
        if (_applyLanguageAsync is null)
        {
            return true;
        }

        try
        {
            return await _applyLanguageAsync(language);
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Language UI apply failed: "
                + exception.GetType().Name);
            return false;
        }
    }

    public void SynchronizeFromCurrentSettings(
        ThemeResult? themeResult = null,
        BackdropResult? backdropResult = null,
        StartupStatus? startupStatus = null,
        bool isStartupSynchronized = true,
        LanguageChangeResult? languageResult = null,
        bool isLanguageSynchronized = true,
        LanguageConsistencyState languageConsistencyState =
            LanguageConsistencyState.Synchronized)
    {
        AppSettings settings = _gameManager.CurrentData.Settings;
        Theme = settings.Theme;
        Language = settings.Language;
        LanguageConsistencyState = languageConsistencyState;
        SelectedBackdrop = settings.Backdrop;
        ActualBackdrop = backdropResult?.ActualBackdrop
            ?? settings.Backdrop;
        AcrylicTintOpacityPercent = settings.AcrylicTintOpacityPercent;
        _lastAppliedAcrylicTintOpacityPercent =
            ActualBackdrop == BackdropKind.Acrylic
                ? backdropResult?.ActualAcrylicTintOpacityPercent
                    ?? settings.AcrylicTintOpacityPercent
                : settings.AcrylicTintOpacityPercent;
        CloseBehavior = settings.CloseBehavior;
        IsStartupEnabled = startupStatus?.IsEnabled
            ?? settings.StartupEnabled;
        AreNotificationsEnabled = settings.NotificationsEnabled;
        NotificationLeadMinutes = settings.NotificationLeadMinutes;

        if (!isStartupSynchronized)
        {
            ShowMessage(
                _appResourceService.GetString(
                    startupStatus is null
                        ? "SettingsStartupStatusCheckFailure"
                        : "SettingsStartupStatusSaveFailure"),
                InfoBarSeverity.Error);
            return;
        }

        if (!isLanguageSynchronized)
        {
            ShowLanguageSynchronizationFailure(
                languageResult?.FailureReason
                ?? LanguageFailureReason.PlatformError);
            return;
        }

        if (backdropResult is { IsRequestedBackdropApplied: false })
        {
            ShowBackdropFallbackMessage(backdropResult);
            return;
        }

        if (themeResult is { IsApplied: false })
        {
            ShowMessage(
                _appResourceService.GetString(
                    "SettingsThemeApplyFailure"),
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
                    _appResourceService.GetString(
                        "SettingsSavedTitle"));
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
            _appResourceService.GetString(
                hasInvalidSchedule
                    ? "SettingsNotificationReconcileInvalidSchedule"
                    : "SettingsNotificationReconcileFailure"),
            InfoBarSeverity.Warning,
            _appResourceService.GetString(
                "SettingsNotificationReconcileTitle"));
    }

    private void ShowNotificationPermissionMessage(
        NotificationPermissionState state)
    {
        string resourceId = state switch
        {
            NotificationPermissionState.DisabledForApplication
                or NotificationPermissionState.DisabledForUser =>
                "SettingsNotificationPermissionDisabled",
            NotificationPermissionState.DisabledByPolicy =>
                "SettingsNotificationPermissionPolicy",
            NotificationPermissionState.DisabledByManifest =>
                "SettingsNotificationPermissionManifest",
            _ => "SettingsNotificationPermissionUnsupported",
        };
        ShowMessage(
            _appResourceService.GetString(resourceId),
            InfoBarSeverity.Warning,
            _appResourceService.GetString(
                "SettingsNotificationPermissionTitle"));
    }

    private void ReportPreparation(SettingsPreparationAction action)
    {
        PreparationRequested?.Invoke(action);
        ShowMessage(
            _appResourceService.GetString(
                "SettingsBackupPrepareMessage"),
            InfoBarSeverity.Informational,
            _appResourceService.GetString(
                "SettingsBackupPrepareTitle"));
    }

    private AppCoordinator GetBackupCoordinator() =>
        _appCoordinator ?? throw new InvalidOperationException(
            _appResourceService.GetString(
                "SettingsBackupUnavailable"));

    private void EnsureBackupIsIdle()
    {
        if (IsBackupBusy)
        {
            throw new InvalidOperationException(
                _appResourceService.GetString(
                    "SettingsBackupAlreadyBusy"));
        }
    }

    private void SynchronizeFromCurrentData(AppCoordinator coordinator)
    {
        AppSettings restored = _gameManager.CurrentData.Settings;
        Theme = restored.Theme;
        SelectedBackdrop = restored.Backdrop;
        ActualBackdrop = coordinator.LastBackdropResult?.ActualBackdrop
            ?? restored.Backdrop;
        AcrylicTintOpacityPercent = restored.AcrylicTintOpacityPercent;
        _lastAppliedAcrylicTintOpacityPercent =
            ActualBackdrop == BackdropKind.Acrylic
                ? coordinator.LastBackdropResult
                    ?.ActualAcrylicTintOpacityPercent
                    ?? restored.AcrylicTintOpacityPercent
                : restored.AcrylicTintOpacityPercent;
        CloseBehavior = restored.CloseBehavior;
        Language = restored.Language;
        LanguageConsistencyState = coordinator.LanguageConsistencyState;
        IsStartupEnabled = restored.StartupEnabled;
        AreNotificationsEnabled = restored.NotificationsEnabled;
        NotificationLeadMinutes = restored.NotificationLeadMinutes;
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
        string resourceId = reason switch
        {
            StartupFailureReason.DisabledByUser =>
                "SettingsStartupDisabledByUser",
            StartupFailureReason.DisabledByPolicy =>
                "SettingsStartupDisabledByPolicy",
            StartupFailureReason.EnabledByPolicy =>
                "SettingsStartupEnabledByPolicy",
            _ => "SettingsStartupChangeFailure",
        };
        ShowMessage(
            _appResourceService.GetString(resourceId),
            InfoBarSeverity.Warning);
    }

    private string GetBackdropFallbackMessage(BackdropResult result)
    {
        string resourceId = result.FallbackReason switch
        {
            BackdropFallbackReason.HighContrast =>
                "SettingsBackdropFallbackHighContrast",
            BackdropFallbackReason.TransparencyDisabled =>
                "SettingsBackdropFallbackTransparencyDisabled",
            BackdropFallbackReason.RemoteSession =>
                "SettingsBackdropFallbackRemoteSession",
            BackdropFallbackReason.Unsupported =>
                "SettingsBackdropFallbackUnsupported",
            BackdropFallbackReason.ApplyFailed =>
                "SettingsBackdropFallbackApplyFailed",
            BackdropFallbackReason.SolidFallbackFailed =>
                "SettingsBackdropFallbackSolidFailed",
            _ => "SettingsBackdropFallbackDefault",
        };
        return _appResourceService.GetString(resourceId);
    }

    private void ShowBackdropFallbackMessage(BackdropResult result)
    {
        bool isSolidFallbackFailed =
            result.FallbackReason == BackdropFallbackReason.SolidFallbackFailed;
        ShowMessage(
            GetBackdropFallbackMessage(result),
            isSolidFallbackFailed
                ? InfoBarSeverity.Error
                : InfoBarSeverity.Warning,
            _appResourceService.GetString(
                isSolidFallbackFailed
                    ? "SettingsErrorTitle"
                    : "SettingsBackdropFallbackTitle"));
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
        if (previousBackdrop == BackdropKind.Acrylic)
        {
            _lastAppliedAcrylicTintOpacityPercent = _gameManager.CurrentData
                .Settings.AcrylicTintOpacityPercent;
            AcrylicTintOpacityPercent =
                _lastAppliedAcrylicTintOpacityPercent;
        }

        try
        {
            BackdropResult rollback = _backdropService.Apply(
                new BackdropRequest(
                    previousBackdrop,
                    _gameManager.CurrentData.Settings
                        .AcrylicTintOpacityPercent));
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

    private async Task<bool> RollbackLanguageAsync(
        AppLanguage previousLanguage)
    {
        try
        {
            await _gameManager.UpdateSettingsAsync(
                    settings => settings with
                    {
                        Language = previousLanguage,
                    },
                    CancellationToken.None);
            return _gameManager.CurrentData.Settings.Language
                == previousLanguage;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Language setting rollback failed: "
                + exception.GetType().Name);
            return false;
        }
    }

    private async Task<bool> ReconcileNotificationsAfterLanguageRollbackAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            NotificationReconcileResult result =
                await _notificationReconciler.ReconcileAsync(
                    _gameManager.Games,
                    _gameManager.CurrentData.Settings,
                    cancellationToken);
            return result.HasFailures;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Notification reconciliation after language rollback failed: "
                + exception.GetType().Name);
            return true;
        }
    }

    private bool RollbackLanguageOverride(AppLanguage previousLanguage)
    {
        try
        {
            return _appLanguageService.SetLanguage(previousLanguage).IsApplied;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Language override rollback failed: "
                + exception.GetType().Name);
            return false;
        }
    }

    private void ShowLanguageChangeFailure(
        LanguageFailureReason reason)
    {
        string resourceId = reason switch
        {
            LanguageFailureReason.Unsupported =>
                "SettingsLanguageUnsupportedRestore",
            _ => "SettingsLanguageApplyFailureRestore",
        };
        ShowMessage(
            _appResourceService.GetString(resourceId),
            InfoBarSeverity.Error);
    }

    private void ShowLanguageSynchronizationFailure(
        LanguageFailureReason reason)
    {
        string resourceId = reason == LanguageFailureReason.Unsupported
            ? "SettingsLanguageUnsupportedSaved"
            : "SettingsLanguageApplyFailureSaved";
        ShowMessage(
            _appResourceService.GetString(resourceId),
            InfoBarSeverity.Warning,
            _appResourceService.GetString(
                "SettingsLanguageInconsistentTitle"));
    }

    private AppearanceRollbackStatus RollbackAcrylicOpacity(
        int previousPercent)
    {
        try
        {
            BackdropResult rollback = _backdropService.Apply(
                new BackdropRequest(BackdropKind.Acrylic, previousPercent));
            ActualBackdrop = rollback.ActualBackdrop;
            if (rollback.IsRequestedBackdropApplied)
            {
                _lastAppliedAcrylicTintOpacityPercent =
                    rollback.ActualAcrylicTintOpacityPercent
                    ?? previousPercent;
                return AppearanceRollbackStatus.Restored;
            }

            return rollback.ActualBackdrop == BackdropKind.Solid
                ? AppearanceRollbackStatus.SafeFallback
                : AppearanceRollbackStatus.Failed;
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Acrylic opacity rollback failed: "
                + exception.GetType().Name);
            return ApplySafeBackdropFallback();
        }
    }

    private AppearanceRollbackStatus ApplySafeBackdropFallback()
    {
        try
        {
            BackdropResult fallback = _backdropService.Apply(
                new BackdropRequest(
                    BackdropKind.Solid,
                    _gameManager.CurrentData.Settings
                        .AcrylicTintOpacityPercent));
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
        string resourceId = rollbackStatus switch
        {
            AppearanceRollbackStatus.Restored =>
                wasCanceled
                    ? "SettingsSaveCanceled"
                    : "SettingsSaveFailure",
            AppearanceRollbackStatus.Unknown =>
                wasCanceled
                    ? "SettingsThemeRollbackUnknownAfterCancel"
                    : "SettingsThemeRollbackUnknown",
            _ =>
                wasCanceled
                    ? "SettingsThemeRollbackFailedAfterCancel"
                    : "SettingsThemeRollbackFailed",
        };

        ShowMessage(
            _appResourceService.GetString(resourceId),
            InfoBarSeverity.Error);
    }

    private void ShowBackdropRollbackMessage(
        AppearanceRollbackStatus rollbackStatus,
        bool wasCanceled)
    {
        string resourceId = rollbackStatus switch
        {
            AppearanceRollbackStatus.Restored =>
                wasCanceled
                    ? "SettingsSaveCanceled"
                    : "SettingsSaveFailure",
            AppearanceRollbackStatus.SafeFallback =>
                wasCanceled
                    ? "SettingsBackdropRollbackSafeFallbackAfterCancel"
                    : "SettingsBackdropRollbackSafeFallback",
            AppearanceRollbackStatus.Unknown =>
                wasCanceled
                    ? "SettingsBackdropRollbackUnknownAfterCancel"
                    : "SettingsBackdropRollbackUnknown",
            _ =>
                wasCanceled
                    ? "SettingsBackdropRollbackFailedAfterCancel"
                    : "SettingsBackdropRollbackFailed",
        };

        ShowMessage(
            _appResourceService.GetString(resourceId),
            InfoBarSeverity.Error);
    }

    private void ShowAcrylicOpacityFailure(
        AppearanceRollbackStatus rollbackStatus)
    {
        string resourceId = rollbackStatus switch
        {
            AppearanceRollbackStatus.Restored =>
                "SettingsAcrylicOpacityFailureRestored",
            AppearanceRollbackStatus.SafeFallback =>
                "SettingsAcrylicOpacityFailureSafeFallback",
            AppearanceRollbackStatus.Unknown =>
                "SettingsAcrylicOpacityFailureUnknown",
            _ =>
                "SettingsAcrylicOpacityFailureRollbackFailed",
        };
        ShowMessage(
            _appResourceService.GetString(resourceId),
            rollbackStatus == AppearanceRollbackStatus.Restored
                ? InfoBarSeverity.Warning
                : InfoBarSeverity.Error,
            _appResourceService.GetString(
                "SettingsAcrylicOpacityFailureTitle"));
    }

    private void ShowAcrylicOpacityRollbackMessage(
        AppearanceRollbackStatus rollbackStatus,
        bool wasCanceled)
    {
        string resourceId = rollbackStatus switch
        {
            AppearanceRollbackStatus.Restored =>
                wasCanceled
                    ? "SettingsSaveCanceled"
                    : "SettingsSaveFailure",
            AppearanceRollbackStatus.SafeFallback =>
                wasCanceled
                    ? "SettingsAcrylicOpacityRollbackSafeFallbackAfterCancel"
                    : "SettingsAcrylicOpacityRollbackSafeFallback",
            AppearanceRollbackStatus.Unknown =>
                wasCanceled
                    ? "SettingsAcrylicOpacityRollbackUnknownAfterCancel"
                    : "SettingsAcrylicOpacityRollbackUnknown",
            _ =>
                wasCanceled
                    ? "SettingsAcrylicOpacityRollbackFailedAfterCancel"
                    : "SettingsAcrylicOpacityRollbackFailed",
        };
        ShowMessage(
            _appResourceService.GetString(resourceId),
            InfoBarSeverity.Error);
    }

    private bool EnsureReady()
    {
        if (IsBackupBusy)
        {
            ShowMessage(
                BackupBusyMessage,
                InfoBarSeverity.Warning,
                _appResourceService.GetString(
                    "SettingsBackupBusyTitle"));
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
                _appResourceService.GetString(
                    "SettingsInitializationFailureTitle"));
        }
        else
        {
            ShowMessage(
                NotReadyMessage,
                InfoBarSeverity.Warning,
                _appResourceService.GetString(
                    "SettingsNotReadyTitle"));
        }

        return false;
    }

    private void ShowMessage(
        string message,
        InfoBarSeverity severity,
        string? title = null)
    {
        InfoBarTitle = title
            ?? _appResourceService.GetString("SettingsErrorTitle");
        InfoBarMessage = message;
        InfoBarSeverity = severity;
        IsInfoBarOpen = true;
    }

    private void CloseInfoBar()
    {
        InfoBarMessage = string.Empty;
        IsInfoBarOpen = false;
    }

    private void SetBackupStatus(string? resourceId)
    {
        _backupStatusResourceId = resourceId;
        BackupStatusText = resourceId is null
            ? string.Empty
            : _appResourceService.GetString(resourceId);
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

    private sealed class PassThroughLanguageService : IAppLanguageService
    {
        public AppLanguage GetEffectiveLanguage() => AppLanguage.Japanese;

        public LanguageChangeResult SetLanguage(AppLanguage language) =>
            new(language, IsApplied: true, LanguageFailureReason.None);
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
