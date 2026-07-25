using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaminaManager.Application;
using StaminaManager.Core.Models;

namespace StaminaManager.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    [ObservableProperty]
    public partial AppPage CurrentPage { get; set; } = AppPage.Overview;

    [ObservableProperty]
    public partial AppDisplayMode DisplayMode { get; set; } =
        AppDisplayMode.Standard;

    [ObservableProperty]
    public partial Guid? SelectedGameId { get; set; }

    public event Action<AppNavigationRequest>? NavigationRequested;

    public void ApplyNavigationRequest(AppNavigationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        CurrentPage = request.Page;
        DisplayMode = request.DisplayMode;
        SelectedGameId = request.GameId;
    }

    [RelayCommand]
    private void ShowOverview() => RequestNavigation(
        new AppNavigationRequest(
            AppPage.Overview,
            AppDisplayMode.Standard,
            SelectedGameId));

    [RelayCommand]
    private void ShowSettings() => RequestNavigation(
        new AppNavigationRequest(
            AppPage.Settings,
            AppDisplayMode.Standard,
            GameId: null));

    [RelayCommand]
    private void EnterCompactMode() => RequestNavigation(
        new AppNavigationRequest(
            AppPage.Overview,
            AppDisplayMode.Compact,
            SelectedGameId));

    private void RequestNavigation(AppNavigationRequest request)
    {
        ApplyNavigationRequest(request);
        NavigationRequested?.Invoke(request);
    }
}
