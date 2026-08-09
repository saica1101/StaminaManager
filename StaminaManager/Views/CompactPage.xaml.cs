using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.ViewModels;
using System.ComponentModel;

namespace StaminaManager.Views;

public sealed partial class CompactPage : Page, INotifyPropertyChanged
{
    private bool _isSynchronizingSelection;

    public CompactPage(
        CompactViewModel viewModel,
        IAppResourceService appResourceService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(appResourceService);
        ViewModel = viewModel;
        InitializeComponent();
        StaminaRingControl.AppResourceService = appResourceService;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ActualThemeChanged += CompactPage_ActualThemeChanged;
        SynchronizeSelection();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public CompactViewModel ViewModel { get; }

    public Brush StatusBrush =>
        (Brush)Microsoft.UI.Xaml.Application.Current.Resources[
            ViewModel.SelectedStatus switch
            {
                StaminaStatus.Safe => "SafeBrush",
                StaminaStatus.Attention => "AttentionBrush",
                StaminaStatus.NearFull
                    or StaminaStatus.Full
                    or StaminaStatus.OverCap => "UrgentBrush",
                _ => throw new ArgumentOutOfRangeException(),
            }];

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static bool HasText(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    public void ApplySelectedGame(Guid? gameId)
    {
        ViewModel.ApplySelectedGame(gameId);
        SynchronizeSelection();
    }

    private async void CompactGameSelector_SelectionChanged(
        object sender,
        SelectionChangedEventArgs args)
    {
        if (_isSynchronizingSelection
            || CompactGameSelector.SelectedItem
                is not GameCardViewModel selected
            || selected.Id == ViewModel.SelectedGame?.Id)
        {
            return;
        }

        try
        {
            await ViewModel.SelectGameAsync(
                selected.Id,
                CancellationToken.None);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or KeyNotFoundException)
        {
            SynchronizeSelection();
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CompactViewModel.SelectedGame))
        {
            SynchronizeSelection();
        }

        if (args.PropertyName == nameof(CompactViewModel.SelectedStatus))
        {
            NotifyPresentationChanged();
        }
    }

    private void CompactPage_ActualThemeChanged(
        FrameworkElement sender,
        object args) => NotifyPresentationChanged();

    private void SynchronizeSelection()
    {
        _isSynchronizingSelection = true;
        try
        {
            CompactGameSelector.SelectedItem = ViewModel.SelectedGame;
        }
        finally
        {
            _isSynchronizingSelection = false;
        }
    }

    private void NotifyPresentationChanged()
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(StatusBrush)));
    }
}
