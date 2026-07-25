using Microsoft.UI.Xaml.Controls;
using StaminaManager.ViewModels;

namespace StaminaManager.Views;

public sealed partial class OverviewPage : Page
{
    public OverviewPage(OverviewViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
    }

    public OverviewViewModel ViewModel { get; }

    public void ShowStartupError() => StartupErrorInfoBar.IsOpen = true;
}
