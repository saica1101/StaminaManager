using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using StaminaManager.Application;
using StaminaManager.Controls;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Storage;
using StaminaManager.ViewModels;
using StaminaManager.Views;
using System.ComponentModel;

namespace StaminaManager;

public sealed partial class MainPage : Page
{
    private readonly OverviewPage _overviewPage;
    private readonly SettingsPage _settingsPage;
    private readonly AboutPage _aboutPage;
    private readonly CompactPage _compactPage;
    private readonly AppCoordinator _coordinator;
    private readonly GameManager _gameManager;
    private readonly IClock _clock;
    private readonly AssetStore _assetStore;
    private readonly IAppResourceService _appResourceService;
    private bool _isSynchronizingSelection;
    private bool _isDialogOpen;
    private bool _isDetached;

    public MainPage(
        ShellViewModel viewModel,
        OverviewPage overviewPage,
        SettingsPage settingsPage,
        AboutPage aboutPage,
        CompactPage compactPage,
        AppCoordinator coordinator,
        GameManager gameManager,
        IClock clock,
        AssetStore assetStore,
        IAppResourceService appResourceService,
        IAppVersionProvider versionProvider)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(overviewPage);
        ArgumentNullException.ThrowIfNull(settingsPage);
        ArgumentNullException.ThrowIfNull(aboutPage);
        ArgumentNullException.ThrowIfNull(compactPage);
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(assetStore);
        ArgumentNullException.ThrowIfNull(appResourceService);
        ArgumentNullException.ThrowIfNull(versionProvider);

        ViewModel = viewModel;
        _overviewPage = overviewPage;
        _settingsPage = settingsPage;
        _aboutPage = aboutPage;
        _compactPage = compactPage;
        _coordinator = coordinator;
        _gameManager = gameManager;
        _clock = clock;
        _assetStore = assetStore;
        _appResourceService = appResourceService;
        VersionText = versionProvider.GetVersion().DisplayVersion;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        _overviewPage.ViewModel.AddGameRequested += OnAddGameRequested;
        _overviewPage.ViewModel.EditGameRequested += OnEditGameRequested;
        _overviewPage.ViewModel.UpdateCurrentRequested += OnOverviewUpdateCurrentRequested;
        _overviewPage.ViewModel.CompactModeRequested +=
            OnCompactModeRequested;
        _compactPage.ViewModel.AddGameRequested += OnAddGameRequested;
        _compactPage.ViewModel.EditGameRequested +=
            OnCompactEditGameRequested;
        _compactPage.ViewModel.ReturnOverviewRequested +=
            OnReturnOverviewRequested;
        ApplyNavigation(ViewModel.CurrentPage);
        ApplyDisplayMode(ViewModel.DisplayMode);
    }

    public ShellViewModel ViewModel { get; }

    public string VersionText { get; }

    public string VersionFooterDisplayText =>
        _appResourceService.Format(
            "VersionFooterTextFormat",
            VersionText);

    public string VersionFooterAutomationName =>
        VersionFooterDisplayText;

    internal void RefreshVersionFooterAutomationProperties()
    {
        string value = VersionFooterAutomationName;
        AutomationProperties.SetName(VersionFooterText, value);
        AutomationProperties.SetHelpText(VersionFooterText, value);
    }

    internal Task FlushPendingSettingsChangesAsync() =>
        _settingsPage.FlushPendingAppearanceChangesAsync();

    internal void Detach()
    {
        if (_isDetached)
        {
            return;
        }

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _overviewPage.ViewModel.AddGameRequested -= OnAddGameRequested;
        _overviewPage.ViewModel.EditGameRequested -= OnEditGameRequested;
        _overviewPage.ViewModel.UpdateCurrentRequested -= OnOverviewUpdateCurrentRequested;
        _overviewPage.ViewModel.CompactModeRequested -=
            OnCompactModeRequested;
        _compactPage.ViewModel.AddGameRequested -= OnAddGameRequested;
        _compactPage.ViewModel.EditGameRequested -=
            OnCompactEditGameRequested;
        _compactPage.ViewModel.ReturnOverviewRequested -=
            OnReturnOverviewRequested;
        _settingsPage.Detach();
        _compactPage.Detach();
        _isDetached = true;
    }

    private void ShellNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        NavigationViewItem? item = args.SelectedItemContainer
            as NavigationViewItem
            ?? args.SelectedItem as NavigationViewItem
            ?? sender.SelectedItem as NavigationViewItem;
        if (_isSynchronizingSelection
            || item is null)
        {
            return;
        }

        switch (item.Tag as string)
        {
            case "Overview":
                ViewModel.ShowOverviewCommand.Execute(null);
                break;
            case "Settings":
                ViewModel.ShowSettingsCommand.Execute(null);
                break;
            case "About":
                ViewModel.ShowAboutCommand.Execute(null);
                break;
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.CurrentPage))
        {
            ApplyNavigation(ViewModel.CurrentPage);
        }

        else if (args.PropertyName == nameof(ShellViewModel.DisplayMode))
        {
            ApplyDisplayMode(ViewModel.DisplayMode);
        }
        else if (args.PropertyName == nameof(ShellViewModel.SelectedGameId)
            && ViewModel.DisplayMode == AppDisplayMode.Compact)
        {
            _compactPage.ApplySelectedGame(ViewModel.SelectedGameId);
        }
    }

    private void ApplyNavigation(AppPage page)
    {
        _isSynchronizingSelection = true;
        try
        {
            NavigationViewItem selectedItem;
            if (page == AppPage.Settings)
            {
                selectedItem = SettingsNavigationItem;
                ShellContentFrame.Content = _settingsPage;
            }
            else if (page == AppPage.About)
            {
                selectedItem = AboutNavigationItem;
                ShellContentFrame.Content = _aboutPage;
            }
            else
            {
                selectedItem = OverviewNavigationItem;
                ShellContentFrame.Content = _overviewPage;
            }

            ShellNavigation.SelectedItem = selectedItem;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private void ApplyDisplayMode(AppDisplayMode displayMode)
    {
        bool isCompact = displayMode == AppDisplayMode.Compact;
        if (!isCompact)
        {
            _compactPage.ResetAlwaysOnTop();
        }
        ShellNavigation.Visibility = isCompact
            ? Visibility.Collapsed
            : Visibility.Visible;
        CompactContentHost.Visibility = isCompact
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompactContentHost.Content = isCompact ? _compactPage : null;
        ApplyVersionFooterVisibility();
        if (isCompact)
        {
            _compactPage.ApplySelectedGame(ViewModel.SelectedGameId);
        }

    }

    private void ShellNavigation_PaneOpened(
        NavigationView sender,
        object args) => ApplyVersionFooterVisibility();

    private void ShellNavigation_PaneClosed(
        NavigationView sender,
        object args) => ApplyVersionFooterVisibility();

    private void ApplyVersionFooterVisibility() =>
        VersionFooterBand.Visibility = GetVersionFooterVisibility(
            ShellNavigation.IsPaneOpen,
            ViewModel.DisplayMode == AppDisplayMode.Compact);

    internal static Visibility GetVersionFooterVisibility(
        bool isPaneOpen,
        bool isCompact) => isPaneOpen && !isCompact
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnAddGameRequested() =>
        _ = ShowGameEditorAsync(gameId: null, focusCurrent: false);

    private void OnEditGameRequested(Guid gameId) =>
        _ = ShowGameEditorAsync(gameId, focusCurrent: false);

    private void OnOverviewUpdateCurrentRequested(Guid gameId) =>
        _ = ShowGameEditorAsync(gameId, focusCurrent: true);

    private void OnCompactEditGameRequested(
        Guid gameId,
        bool focusCurrent) =>
        _ = ShowGameEditorAsync(gameId, focusCurrent);

    private async void OnCompactModeRequested()
    {
        try
        {
            await _coordinator.ChangeDisplayModeAsync(
                AppDisplayMode.Compact,
                _gameManager.CurrentData.Settings.SelectedCompactGameId,
                CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            await _overviewPage.ViewModel.ShowErrorAsync(exception);
        }
    }

    private async void OnReturnOverviewRequested()
    {
        try
        {
            await _coordinator.ChangeDisplayModeAsync(
                AppDisplayMode.Standard,
                _compactPage.ViewModel.SelectedGame?.Id,
                CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or InvalidOperationException)
        {
            _compactPage.ViewModel.ShowErrorResource(
                "ReturnOverviewSaveError");
        }
    }

    private async Task ShowGameEditorAsync(
        Guid? gameId,
        bool focusCurrent)
    {
        if (_isDialogOpen || XamlRoot is null)
        {
            return;
        }

        GameEntry? entry = gameId is Guid id
            ? _gameManager.Games.FirstOrDefault(game => game.Id == id)
            : null;
        if (gameId is not null && entry is null)
        {
            await _overviewPage.ViewModel.ShowErrorAsync(
                new KeyNotFoundException());
            return;
        }

        _isDialogOpen = true;
        try
        {
            GameEditorViewModel editor = new(
                _gameManager,
                _clock,
                _appResourceService,
                entry);
            GameEditorDialog dialog = new(
                editor,
                _assetStore,
                _gameManager,
                _appResourceService,
                ((App)Microsoft.UI.Xaml.Application.Current)
                    .MainWindowHandle)
            {
                RequestedTheme = ActualTheme,
            };
            _ = await dialog.ShowAsync(
                XamlRoot,
                focusCurrent,
                CancellationToken.None);
        }
        finally
        {
            _isDialogOpen = false;
        }
    }
}
