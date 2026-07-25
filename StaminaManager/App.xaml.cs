using Windows.ApplicationModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Persistence;
using StaminaManager.Infrastructure.Windows;
using StaminaManager.ViewModels;
using StaminaManager.Views;
using System.Diagnostics;

namespace StaminaManager;

public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? _window;
    private AppCoordinator? _coordinator;
    private GameManager? _gameManager;
    private OverviewViewModel? _overviewViewModel;
    private OverviewPage? _overviewPage;
    private ShellViewModel? _shellViewModel;
    private TimerCoordinator? _timerCoordinator;
    private string _startupStage = "NotStarted";
    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(
        Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
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

        try
        {
            CreateCompositionRoot();
            _window!.Activate();
            DataLoadResult loadResult = await _coordinator!.InitializeAsync(
                CancellationToken.None);
            if (loadResult.Status == DataLoadStatus.Corrupt
                || !_gameManager!.IsInitialized)
            {
                _overviewPage!.ShowStartupError();
                return;
            }

            await _overviewViewModel!.SetLoadingAsync(false);
            await _timerCoordinator!.SetVisibleAsync(true);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"StaminaManager startup failed at {_startupStage}: "
                + exception.GetType().Name);
            _overviewPage?.ShowStartupError();
            _window?.Activate();
        }
    }

    private void CreateCompositionRoot()
    {
        DispatcherQueue dispatcherQueue =
            DispatcherQueue.GetForCurrentThread()
            ?? throw new InvalidOperationException(
                "UI DispatcherQueueを取得できませんでした。");
        IUiDispatcher uiDispatcher = new DispatcherQueueUiDispatcher(
            dispatcherQueue);
        IClock clock = new SystemClock();
        IAppDataPathProvider pathProvider = new AppDataPathProvider();
        ILocalDataStore dataStore = new LocalDataStore(pathProvider);
        AppSettings initialSettings = AppSettings.CreateDefault(
            RequestedTheme == ApplicationTheme.Dark
                ? AppTheme.Dark
                : AppTheme.Light);

        _gameManager = new GameManager(dataStore, clock, initialSettings);
        _shellViewModel = new ShellViewModel();
        _overviewViewModel = new OverviewViewModel(
            _gameManager,
            clock,
            uiDispatcher);
        _timerCoordinator = new TimerCoordinator(
            clock,
            new SystemTickSource(),
            uiDispatcher,
            _overviewViewModel.RefreshOnUiThread);
        _coordinator = new AppCoordinator(
            dataStore,
            _gameManager,
            uiDispatcher);
        _coordinator.NavigationRequested += OnNavigationRequested;

        _startupStage = "OverviewPage";
        _overviewPage = new OverviewPage(_overviewViewModel);
        _startupStage = "SettingsPage";
        SettingsPage settingsPage = new();
        _startupStage = "MainPage";
        MainPage mainPage = new(
            _shellViewModel,
            _overviewPage,
            settingsPage);
        _startupStage = "MainWindow";
        _window = new MainWindow(mainPage);
        _startupStage = "Composed";
    }

    private void OnNavigationRequested(
        object? sender,
        AppNavigationRequest request)
    {
        _shellViewModel?.ApplyNavigationRequest(request);
    }
}
