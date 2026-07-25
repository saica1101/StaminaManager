using Microsoft.UI.Xaml.Controls;
using StaminaManager.Application;
using StaminaManager.ViewModels;
using StaminaManager.Views;
using System.ComponentModel;

namespace StaminaManager;

public sealed partial class MainPage : Page
{
    private readonly OverviewPage _overviewPage;
    private readonly SettingsPage _settingsPage;
    private bool _isSynchronizingSelection;

    public MainPage(
        ShellViewModel viewModel,
        OverviewPage overviewPage,
        SettingsPage settingsPage)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(overviewPage);
        ArgumentNullException.ThrowIfNull(settingsPage);

        ViewModel = viewModel;
        _overviewPage = overviewPage;
        _settingsPage = settingsPage;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ApplyNavigation(ViewModel.CurrentPage);
    }

    public ShellViewModel ViewModel { get; }

    private void ShellNavigation_SelectionChanged(
        NavigationView sender,
        NavigationViewSelectionChangedEventArgs args)
    {
        if (_isSynchronizingSelection
            || args.SelectedItemContainer is not NavigationViewItem item)
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
}
