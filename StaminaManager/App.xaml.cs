using Windows.ApplicationModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using Microsoft.Windows.AppLifecycle;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Persistence;
using StaminaManager.Infrastructure.Storage;
using StaminaManager.Infrastructure.Windows;
using StaminaManager.ViewModels;
using StaminaManager.Views;
using System.Diagnostics;
using System.Runtime.ExceptionServices;

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
    private ShellViewModel? _shellViewModel;
    private TimerCoordinator? _timerCoordinator;
    private IStartupService? _startupService;
    private IWindowStateService? _windowStateService;
    private ITrayService? _trayService;
    private DispatcherQueue? _dispatcherQueue;
    private Window? _fallbackWindow;
    private string _startupStage = "NotStarted";
    public App()
    {
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
            DataLoadResult loadResult = await _coordinator!.InitializeAsync(
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
                _coordinator.LastThemeResult,
                _coordinator.LastBackdropResult,
                _coordinator.LastStartupStatus,
                _coordinator.IsStartupSynchronized);
            _settingsViewModel.MarkReady();

            await _overviewViewModel!.SetLoadingAsync(false);
            await _timerCoordinator!.SetVisibleAsync(true);
        }
        catch (Exception exception)
        {
            _settingsViewModel?.MarkFailed();
            await HandleLaunchFailureAsync(exception);
        }
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
        // Task 11で通知activationの引数を解決するため、元の引数を保持する。
        _ = activationArguments;
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

        LaunchFailureText text = LoadLaunchFailureText();
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

    private static LaunchFailureText LoadLaunchFailureText()
    {
        LaunchFailureText fallback = new(
            "Stamina Manager",
            "Stamina Managerを起動できませんでした",
            "起動中に問題が発生しました。アプリを閉じて、もう一度お試しください。",
            "閉じる");

        try
        {
            ResourceLoader loader = new();
            return new LaunchFailureText(
                GetResourceOrFallback(
                    loader,
                    "StartupFailureWindowTitle",
                    fallback.WindowTitle),
                GetResourceOrFallback(
                    loader,
                    "StartupFailureHeading",
                    fallback.Heading),
                GetResourceOrFallback(
                    loader,
                    "StartupFailureMessage",
                    fallback.Message),
                GetResourceOrFallback(
                    loader,
                    "StartupFailureCloseButton",
                    fallback.CloseButton));
        }
        catch (Exception resourceException)
        {
            Debug.WriteLine(
                "StaminaManager fallback resources failed: "
                + resourceException.GetType().Name);
            return fallback;
        }
    }

    private static string GetResourceOrFallback(
        ResourceLoader loader,
        string resourceId,
        string fallback)
    {
        string value = loader.GetString(resourceId);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
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
        IAppDataPathProvider pathProvider = new AppDataPathProvider();
        ILocalDataStore dataStore = new LocalDataStore(pathProvider);
        AssetStore assetStore = new(pathProvider);
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
        AppSettings initialSettings = AppSettings.CreateDefault(
            themeService.ResolveInitialTheme());

        _gameManager = new GameManager(dataStore, clock, initialSettings);
        _shellViewModel = new ShellViewModel();
        _overviewViewModel = new OverviewViewModel(
            _gameManager,
            clock,
            uiDispatcher);
        _coordinator = new AppCoordinator(
            dataStore,
            _gameManager,
            uiDispatcher,
            themeService,
            backdropService,
            _startupService,
            _windowStateService);
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
        _settingsViewModel = new SettingsViewModel(
            _gameManager,
            themeService,
            backdropService,
            _startupService);

        _startupStage = "OverviewPage";
        _overviewPage = new OverviewPage(_overviewViewModel);
        _startupStage = "SettingsPage";
        SettingsPage settingsPage = new(_settingsViewModel);
        _startupStage = "CompactPage";
        CompactPage compactPage = new(_compactViewModel);
        _startupStage = "MainPage";
        MainPage mainPage = new(
            _shellViewModel,
            _overviewPage,
            settingsPage,
            compactPage,
            _coordinator,
            _gameManager,
            clock,
            assetStore);
        _startupStage = "MainWindow";
        _window = new MainWindow(mainPage);
        _window.ConfigureLifecycle(
            _windowStateService,
            _trayService,
            () => _gameManager.CurrentData.Settings.CloseBehavior);
        _trayService.Initialize();
        _startupStage = "Composed";
    }

    private void OnNavigationRequested(
        object? sender,
        AppNavigationRequest request)
    {
        _shellViewModel?.ApplyNavigationRequest(request);
    }

    private sealed record LaunchFailureText(
        string WindowTitle,
        string Heading,
        string Message,
        string CloseButton);
}
