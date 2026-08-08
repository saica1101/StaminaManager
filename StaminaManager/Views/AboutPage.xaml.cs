using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StaminaManager.ViewModels;

namespace StaminaManager.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage(AboutViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ViewModel = viewModel;
        InitializeComponent();
    }

    public AboutViewModel ViewModel { get; }

    private void AboutInfoBar_Closed(
        InfoBar sender,
        InfoBarClosedEventArgs args) => ViewModel.DismissInfoBar();
}
