using Windows.ApplicationModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Persistence;
using StaminaManager.Infrastructure.Backup;
using StaminaManager.Infrastructure.Notifications;
using StaminaManager.Infrastructure.Resources;
using StaminaManager.Infrastructure.Storage;
using StaminaManager.Infrastructure.Windows;
using StaminaManager.ViewModels;
using StaminaManager.Views;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using TrayWindowVisibilityChangedEventArgs =
    StaminaManager.Core.Abstractions.WindowVisibilityChangedEventArgs;

namespace StaminaManager;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? _window;
    private AppCoordinator? _coordinator;
    private GameManager? _gameManager;
    private OverviewViewModel? _overviewViewModel;
    private CompactViewModel? _compactViewModel;
    private SettingsViewModel? _settingsViewModel;
    private OverviewPage? _overviewPage;
    private MainPage? _mainPage;
    private ShellViewModel? _shellViewModel;
    private TimerCoordinator? _timerCoordinator;
    private TimerVisibilityController? _timerVisibilityController;
    private IStartupService? _startupService;
    private IWindowStateService? _windowStateService;
    private ITrayService? _trayService;
    private readonly INotificationScheduler _notificationScheduler;
    private readonly IAppResourceService _appResourceService;
    private DispatcherQueue? _dispatcherQueue;
    private Window? _fallbackWindow;
    private readonly NotificationActivationQueue
        _notificationActivationQueue = new();
    private string _startupStage = "NotStarted";
    private bool _isShutdownRequested;
    public App()
        : this(
            new WindowsNotificationScheduler(),
            new AppResourceService())
    {
    }

    internal App(INotificationScheduler notificationScheduler)
        : this(notificationScheduler, new AppResourceService())
    {
    }

    internal App(
        INotificationScheduler notificationScheduler,
        IAppResourceService appResourceService)
    {
        ArgumentNullException.ThrowIfNull(notificationScheduler);
        ArgumentNullException.ThrowIfNull(appResourceService);
        _notificationScheduler = notificationScheduler;
        _appResourceService = appResourceService;
        _notificationScheduler.ActivationRequested +=
            OnNotificationActivationRequested;
        InitializeComponent();
    }

    public nint MainWindowHandle => _window is null
        ? 0
        : WinRT.Interop.WindowNative.GetWindowHandle(_window);

    protected override async void OnLaunched(
        Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            if (_window is not null)
            {
                _window.Activate();
                if (_coordinator is not null)
                {
                    await _coordinator.RouteActivationAsync(gameId: null);
                }

                return;
            }

            CreateCompositionRoot();
            DataLoadResult loadResult = await InitializeForLaunchAsync(
                _coordinator!,
                _overviewViewModel!,
                CancellationToken.None);
            ActivationRouter.Attach(HandleRedirectedActivation);
            _window!.Activate();
            if (loadResult.Status == DataLoadStatus.Corrupt
                || !_gameManager!.IsInitialized)
            {
                _settingsViewModel!.MarkFailed();
                _overviewPage!.ShowStartupError();
                return;
            }

            _settingsViewModel!.SynchronizeFromCurrentSettings(
                _coordinator!.LastThemeResult,
                _coordinator.LastBackdropResult,
                _coordinator.LastStartupStatus,
                _coordinator.IsStartupSynchronized,
                _coordinator.LastLanguageResult,
                _coordinator.IsLanguageSynchronized,
                _coordinator.LanguageConsistencyState);
            await _settingsViewModel.RefreshNotificationAvailabilityAsync();
            _settingsViewModel.MarkReady();

            await _overviewViewModel!.SetLoadingAsync(false);
            await _timerVisibilityController!.SetWindowShownAsync(true);
            await DrainNotificationActivationsAsync();
        }
        catch (Exception exception)
        {
            _settingsViewModel?.MarkFailed();
            await HandleLaunchFailureAsync(exception);
        }
    }

    internal static async Task<DataLoadResult> InitializeForLaunchAsync(
        AppCoordinator coordinator,
        OverviewViewModel overviewViewModel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(overviewViewModel);
        DataLoadResult result = await coordinator.InitializeAsync(
            cancellationToken);
        await overviewViewModel.ShowDataLoadWarningAsync(
            result.Warning,
            cancellationToken);
        await overviewViewModel.ShowStartupRecoveryAsync(
            coordinator.StartupRecovery,
            cancellationToken);
        return result;
    }

    private void HandleRedirectedActivation(
        AppActivationArguments activationArguments)
    {
        DispatcherQueue? dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue is null)
        {
            return;
        }

        if (dispatcherQueue.HasThreadAccess)
        {
            _ = RestoreForActivationAsync(activationArguments);
            return;
        }

        _ = dispatcherQueue.TryEnqueue(
            () => _ = RestoreForActivationAsync(activationArguments));
    }

    private async Task RestoreForActivationAsync(
        AppActivationArguments activationArguments)
    {
        if (activationArguments.Kind
            == ExtendedActivationKind.AppNotification
            && activationArguments.Data
                is AppNotificationActivatedEventArgs notificationArgs
            && WindowsNotificationScheduler.TryParseGameId(
                notificationArgs.Argument,
                out Guid gameId))
        {
            QueueNotificationActivation(gameId);
            await DrainNotificationActivationsAsync();
            return;
        }

        _window?.RestoreAndActivate();
        if (_coordinator is not null)
        {
            await _coordinator.RouteActivationAsync(gameId: null);
        }
    }

    private async Task HandleLaunchFailureAsync(Exception launchException)
    {
        Debug.WriteLine(
            $"StaminaManager startup failed at {_startupStage}: "
            + launchException.GetType().Name);

        if (_window is not null
            && _overviewPage is not null
            && _overviewViewModel is not null)
        {
            try
            {
                await _overviewViewModel.ShowErrorAsync(launchException);
                _overviewPage.ShowStartupError();
                _window.Activate();
                return;
            }
            catch (Exception presentationException)
            {
                Debug.WriteLine(
                    "StaminaManager startup error presentation failed: "
                    + presentationException.GetType().Name);
            }
        }

        try
        {
            if (TryShowFallbackWindow())
            {
                return;
            }
        }
        catch (Exception fallbackException)
        {
            Debug.WriteLine(
                "StaminaManager fallback window failed: "
                + fallbackException.GetType().Name);
        }

        ExceptionDispatchInfo.Capture(launchException).Throw();
    }

    private bool TryShowFallbackWindow()
    {
        if (_fallbackWindow is not null)
        {
            _fallbackWindow.Activate();
            return true;
        }

        LaunchFailureText text = LoadLaunchFailureText(_appResourceService);
        Window fallbackWindow = new()
        {
            Title = text.WindowTitle,
        };
        TextBlock heading = new()
        {
            Text = text.Heading,
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap,
        };
        TextBlock message = new()
        {
            Text = text.Message,
            TextWrapping = TextWrapping.Wrap,
        };
        Button closeButton = new()
        {
            Content = text.CloseButton,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        AutomationProperties.SetAutomationId(
            closeButton,
            "StartupFailureCloseButton");
        AutomationProperties.SetName(closeButton, text.CloseButton);

        StackPanel content = new()
        {
            Padding = new Thickness(32),
            Spacing = 16,
            MaxWidth = 560,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(heading);
        content.Children.Add(message);
        content.Children.Add(closeButton);
        fallbackWindow.Content = content;
        closeButton.Click += (_, _) =>
        {
            fallbackWindow.Close();
            _fallbackWindow = null;
        };

        _fallbackWindow = fallbackWindow;
        fallbackWindow.Activate();
        closeButton.Focus(FocusState.Programmatic);
        return true;
    }

    private static LaunchFailureText LoadLaunchFailureText(
        IAppResourceService resources)
    {
        LaunchFailureText fallback = new(
            "Stamina Manager",
            "Stamina Managerを起動できませんでした",
            "起動中に問題が発生しました。アプリを閉じて、もう一度お試しください。",
            "閉じる");

        return new LaunchFailureText(
            GetResourceOrFallback(
                resources,
                "StartupFailureWindowTitle",
                fallback.WindowTitle),
            GetResourceOrFallback(
                resources,
                "StartupFailureHeading",
                fallback.Heading),
            GetResourceOrFallback(
                resources,
                "StartupFailureMessage",
                fallback.Message),
            GetResourceOrFallback(
                resources,
                "StartupFailureCloseButton",
                fallback.CloseButton));
    }

    private static string GetResourceOrFallback(
        IAppResourceService resources,
        string resourceId,
        string fallback)
    {
        string value = resources.GetString(resourceId);
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, resourceId, StringComparison.Ordinal)
            ? fallback
            : value;
    }

    private void CreateCompositionRoot()
    {
        DispatcherQueue dispatcherQueue =
            DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "UI DispatcherQueueを取得できませんでした。");
        _dispatcherQueue = dispatcherQueue;
        IUiDispatcher uiDispatcher = new DispatcherQueueUiDispatcher(
            dispatcherQueue);
        IClock clock = new SystemClock();
        IAppLanguageService appLanguageService = new AppLanguageService();
        AppLanguage sessionLanguage = appLanguageService.GetEffectiveLanguage();
        IAppDataPathProvider pathProvider = new AppDataPathProvider();
        ILocalDataStore dataStore = new LocalDataStore(
            pathProvider,
            appLanguageService);
        AssetStore assetStore = new(pathProvider);
        INotificationLedgerStore notificationLedgerStore =
            new NotificationLedgerStore(pathProvider);
        IThemeService themeService = new ThemeService(
            new FrameworkElementThemeTarget(
                () => _window?.Content as FrameworkElement),
            () => RequestedTheme);
        IBackdropService backdropService = new BackdropService(
            new MainWindowBackdropTarget(() => _window),
            new WindowsBackdropEnvironment(
                () => _window?.AppWindow.Id));
        _startupService = new StartupService();
        _windowStateService = new WindowStateService(() => _window);
        _trayService = new TrayService(() => _window);
        INotificationPermissionService notificationPermissionService =
            new NotificationPermissionService();
        ISettingsLauncher settingsLauncher = new WindowsSettingsLauncher();
        IAppVersionProvider versionProvider = new AppVersionProvider();
        IExternalUriLauncher externalUriLauncher = new ExternalUriLauncher();
        AppSettings initialSettings = AppSettings.CreateDefault(
            themeService.ResolveInitialTheme(),
            sessionLanguage);

        _gameManager = new GameManager(dataStore, clock, initialSettings);
        _shellViewModel = new ShellViewModel();
        _overviewViewModel = new OverviewViewModel(
            _gameManager,
            clock,
            uiDispatcher);
        NotificationCoordinator notificationCoordinator = new(
            _notificationScheduler,
            notificationLedgerStore,
            clock);
        IBackupService backupService = new BackupCoordinator(
            new SafeZipReader(appLanguageService),
            dataStore,
            pathProvider);
        RestoreCoordinator restoreCoordinator = new(
            backupService,
            _gameManager);
        _coordinator = new AppCoordinator(
            dataStore,
            _gameManager,
            uiDispatcher,
            themeService,
            backdropService,
            _startupService,
            _windowStateService,
            notificationCoordinator,
            restoreCoordinator,
            appLanguageService,
            sessionLanguage);
        _coordinator.NavigationRequested += OnNavigationRequested;
        _compactViewModel = new CompactViewModel(
            _gameManager,
            clock,
            _coordinator,
            uiDispatcher);
        _timerCoordinator = new TimerCoordinator(
            clock,
            new SystemTickSource(),
            uiDispatcher,
            nowUtc =>
            {
                _overviewViewModel.RefreshOnUiThread(nowUtc);
                _compactViewModel.RefreshOnUiThread(nowUtc);
            });
        _timerVisibilityController = new TimerVisibilityController(
            _timerCoordinator);
        _shellViewModel.PropertyChanged +=
            OnShellViewModelPropertyChanged;
        _trayService.WindowVisibilityChanged +=
            OnTrayWindowVisibilityChanged;
        _settingsViewModel = new SettingsViewModel(
            _gameManager,
            themeService,
            backdropService,
            _startupService,
            notificationCoordinator,
            notificationPermissionService,
            settingsLauncher,
            _coordinator,
            appLanguageService,
            sessionLanguage);

        _startupStage = "OverviewPage";
        _overviewPage = new OverviewPage(_overviewViewModel);
        _startupStage = "SettingsPage";
        SettingsPage settingsPage = new(_settingsViewModel);
        _startupStage = "AboutPage";
        AboutPage aboutPage = new(
            new AboutViewModel(versionProvider, externalUriLauncher));
        _startupStage = "CompactPage";
        CompactPage compactPage = new(_compactViewModel);
        _startupStage = "MainPage";
        MainPage mainPage = new(
            _shellViewModel,
            _overviewPage,
            settingsPage,
            aboutPage,
            compactPage,
            _coordinator,
            _gameManager,
            clock,
            assetStore,
            _appResourceService,
            versionProvider);
        _mainPage = mainPage;
        _startupStage = "MainWindow";
        _window = new MainWindow(mainPage);
        _window.ConfigureLifecycle(
            _windowStateService,
            _trayService,
            () => _gameManager.CurrentData.Settings.CloseBehavior,
            ShutdownAsync);
        try
        {
            _notificationScheduler.Initialize();
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Notification registration failed: "
                + exception.GetType().Name);
        }

        _trayService.Initialize();
        _startupStage = "Composed";
    }

    private void OnNavigationRequested(
        object? sender,
        AppNavigationRequest request)
    {
        _shellViewModel?.ApplyNavigationRequest(request);
    }

    private async void OnShellViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ShellViewModel.CurrentPage)
            || _shellViewModel is null
            || _timerVisibilityController is null)
        {
            return;
        }

        await UpdateTimerVisibilityAsync(
            () => _timerVisibilityController.SetCurrentPageAsync(
                _shellViewModel.CurrentPage));
    }

    private async void OnTrayWindowVisibilityChanged(
        object? sender,
        TrayWindowVisibilityChangedEventArgs args)
    {
        if (_timerVisibilityController is null)
        {
            return;
        }

        await UpdateTimerVisibilityAsync(
            () => _timerVisibilityController.SetWindowShownAsync(
                args.IsShown));
    }

    private async Task UpdateTimerVisibilityAsync(
        Func<Task> updateAsync)
    {
        try
        {
            await updateAsync();
        }
        catch (ObjectDisposedException) when (_isShutdownRequested)
        {
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Timer visibility update failed: "
                + exception.GetType().Name);
            if (_overviewViewModel is not null)
            {
                await _overviewViewModel.ShowErrorAsync(exception);
            }
        }
    }

    private void OnNotificationActivationRequested(
        object? sender,
        NotificationActivationEventArgs args)
    {
        QueueNotificationActivation(args.GameId);
        DispatcherQueue? dispatcherQueue = _dispatcherQueue;
        if (dispatcherQueue is null)
        {
            return;
        }

        if (dispatcherQueue.HasThreadAccess)
        {
            _ = DrainNotificationActivationsAsync();
            return;
        }

        _ = dispatcherQueue.TryEnqueue(
            () => _ = DrainNotificationActivationsAsync());
    }

    private void QueueNotificationActivation(Guid gameId)
    {
        _notificationActivationQueue.Enqueue(gameId);
    }

    private async Task DrainNotificationActivationsAsync()
    {
        if (_coordinator?.IsInitialized != true
            || _overviewPage is null
            || _window is null
            || _overviewViewModel is null)
        {
            return;
        }

        AppCoordinator coordinator = _coordinator;
        OverviewPage overviewPage = _overviewPage;
        MainWindow window = _window;
        OverviewViewModel overviewViewModel = _overviewViewModel;
        await _notificationActivationQueue.DrainAsync(
            async gameId =>
            {
                window.RestoreAndActivate();
                await coordinator.RouteActivationAsync(gameId);
                await overviewPage.FocusGameAsync(gameId);
            },
            async (_, exception) =>
            {
                Debug.WriteLine(
                    "Notification activation failed: "
                    + exception.GetType().Name);
                await overviewViewModel.ShowErrorAsync(exception);
            });
    }

    private void DisposeNotificationLifecycle()
    {
        _notificationScheduler.ActivationRequested -=
            OnNotificationActivationRequested;
        _notificationScheduler.Dispose();
    }

    private async Task ShutdownAsync()
    {
        _isShutdownRequested = true;
        if (_shellViewModel is not null)
        {
            _shellViewModel.PropertyChanged -=
                OnShellViewModelPropertyChanged;
        }

        if (_trayService is not null)
        {
            _trayService.WindowVisibilityChanged -=
                OnTrayWindowVisibilityChanged;
        }

        try
        {
            if (_mainPage is not null)
            {
                await _mainPage.FlushPendingSettingsChangesAsync();
            }

            if (_timerVisibilityController is not null)
            {
                await _timerVisibilityController.DisposeAsync();
            }
        }
        finally
        {
            DisposeNotificationLifecycle();
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

    private sealed record LaunchFailureText(
        string WindowTitle,
        string Heading,
        string Message,
        string CloseButton);
}
