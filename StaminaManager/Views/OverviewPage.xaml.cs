using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using StaminaManager.Core.Calculations;
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

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    private void OverviewItems_SizeChanged(
        object sender,
        SizeChangedEventArgs args)
    {
        double contentWidth = args.NewSize.Width;
        int columns = OverviewLayoutPolicy.GetColumns(contentWidth);
        const double gap = 12d;
        double totalGap = gap * (columns - 1);
        OverviewUniformGridLayout.MaximumRowsOrColumns = columns;
        OverviewUniformGridLayout.MinItemWidth = Math.Max(
            1d,
            (contentWidth - totalGap) / columns);
    }
}
