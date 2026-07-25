using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StaminaManager.Core.Models;
using StaminaManager.ViewModels;

namespace StaminaManager.Views;

public sealed partial class SettingsPage : Page
{
    private bool _isSynchronizingControls = true;
    private bool _areControlEventsAttached;

    public SettingsPage(SettingsViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
        Loaded += SettingsPage_Loaded;
    }

    public SettingsViewModel ViewModel { get; }

    private void SettingsPage_Loaded(
        object sender,
        RoutedEventArgs args)
    {
        SynchronizeControls();
        if (_areControlEventsAttached)
        {
            return;
        }

        ThemeToggle.Toggled += ThemeToggle_Toggled;
        BackdropSelector.SelectionChanged +=
            BackdropSelector_SelectionChanged;
        CloseBehaviorSelector.SelectionChanged +=
            CloseBehaviorSelector_SelectionChanged;
        StartupToggle.Toggled += StartupToggle_Toggled;
        NotificationsToggle.Toggled += NotificationsToggle_Toggled;
        NotificationLeadInput.ValueChanged +=
            NotificationLeadInput_ValueChanged;
        _areControlEventsAttached = true;
    }

    private async void ThemeToggle_Toggled(
        object sender,
        RoutedEventArgs args)
    {
        if (_isSynchronizingControls)
        {
            return;
        }

        await ViewModel.SetThemeAsync(
            ThemeToggle.IsOn ? AppTheme.Dark : AppTheme.Light);
        SynchronizeControls();
    }

    private async void BackdropSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_isSynchronizingControls
            || BackdropSelector.SelectedIndex < 0)
        {
            return;
        }

        await ViewModel.SetBackdropAsync(
            (BackdropKind)BackdropSelector.SelectedIndex);
        SynchronizeControls();
    }

    private async void CloseBehaviorSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_isSynchronizingControls
            || CloseBehaviorSelector.SelectedIndex < 0)
        {
            return;
        }

        await ViewModel.SetCloseBehaviorAsync(
            (CloseBehavior)CloseBehaviorSelector.SelectedIndex);
        SynchronizeControls();
    }

    private async void StartupToggle_Toggled(
        object sender,
        RoutedEventArgs args)
    {
        if (_isSynchronizingControls)
        {
            return;
        }

        await ViewModel.SetStartupEnabledAsync(StartupToggle.IsOn);
        SynchronizeControls();
    }

    private async void NotificationsToggle_Toggled(
        object sender,
        RoutedEventArgs args)
    {
        if (_isSynchronizingControls)
        {
            return;
        }

        await ViewModel.SetNotificationsEnabledAsync(
            NotificationsToggle.IsOn);
        SynchronizeControls();
    }

    private async void NotificationLeadInput_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        if (_isSynchronizingControls)
        {
            return;
        }

        await ViewModel.SetNotificationLeadMinutesAsync(args.NewValue);
        SynchronizeControls();
    }

    private void OpenWindowsNotificationSettingsButton_Click(
        object sender,
        RoutedEventArgs args) =>
        ViewModel.PrepareWindowsNotificationSettings();

    private void ExportBackupButton_Click(
        object sender,
        RoutedEventArgs args) => ViewModel.PrepareBackupExport();

    private void ImportBackupButton_Click(
        object sender,
        RoutedEventArgs args) => ViewModel.PrepareBackupImport();

    private void SettingsInfoBar_Closed(
        InfoBar sender,
        InfoBarClosedEventArgs args) => ViewModel.DismissInfoBar();

    private void SynchronizeControls()
    {
        _isSynchronizingControls = true;
        try
        {
            ThemeToggle.IsOn = ViewModel.IsDarkTheme;
            BackdropSelector.SelectedIndex =
                ViewModel.SelectedBackdropIndex;
            CloseBehaviorSelector.SelectedIndex =
                ViewModel.CloseBehaviorIndex;
            StartupToggle.IsOn = ViewModel.IsStartupEnabled;
            NotificationsToggle.IsOn =
                ViewModel.AreNotificationsEnabled;
            NotificationLeadInput.Value =
                ViewModel.NotificationLeadMinutes;
        }
        finally
        {
            _isSynchronizingControls = false;
        }
    }
}
