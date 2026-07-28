using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using StaminaManager.Core.Calculations;
using StaminaManager.ViewModels;
using StaminaManager.Controls;

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

    private void RecoveryInfoBar_Closed(
        InfoBar sender,
        InfoBarClosedEventArgs args) => ViewModel.CloseRecoveryInfoBar();

    public async Task<bool> FocusGameAsync(Guid gameId)
    {
        if (!IsLoaded)
        {
            await WaitForLoadedAsync();
        }

        int index = -1;
        for (int itemIndex = 0;
            itemIndex < ViewModel.OverviewItems.Count;
            itemIndex++)
        {
            if (ViewModel.OverviewItems[itemIndex]
                is GameCardViewModel game
                && game.Id == gameId)
            {
                index = itemIndex;
                break;
            }
        }

        if (index < 0)
        {
            await ViewModel.ShowNotificationTargetMissingAsync();
            return false;
        }

        UpdateLayout();
        UIElement element = OverviewItems.GetOrCreateElement(index);
        element.StartBringIntoView(new BringIntoViewOptions
        {
            AnimationDesired = false,
            VerticalAlignmentRatio = 0.5,
        });
        await Task.Yield();
        return element is GameCardControl card && card.FocusCard();
    }

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

    private Task WaitForLoadedAsync()
    {
        TaskCompletionSource completion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? handler = null;
        handler = (_, _) =>
        {
            Loaded -= handler;
            completion.TrySetResult();
        };
        Loaded += handler;
        return completion.Task;
    }
}
