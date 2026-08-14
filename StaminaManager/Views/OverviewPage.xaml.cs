using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Abstractions;
using StaminaManager.ViewModels;
using StaminaManager.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace StaminaManager.Views;

public sealed partial class OverviewPage : Page
{
    private readonly IAppResourceService _appResourceService;
    private Guid? _draggedGameId;

    public OverviewPage(
        OverviewViewModel viewModel,
        IAppResourceService appResourceService)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(appResourceService);
        ViewModel = viewModel;
        _appResourceService = appResourceService;
        InitializeComponent();
    }

    public OverviewViewModel ViewModel { get; }

    public void ShowStartupError() => StartupErrorInfoBar.IsOpen = true;

    private void RecoveryInfoBar_Closed(
        InfoBar sender,
        InfoBarClosedEventArgs args) => ViewModel.CloseRecoveryInfoBar();

    private void DataLoadWarningInfoBar_Closed(
        InfoBar sender,
        InfoBarClosedEventArgs args) =>
        ViewModel.CloseDataLoadWarningInfoBar();

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

    private void GameCard_DragStarting(
        UIElement sender,
        DragStartingEventArgs args)
    {
        if (sender is not GameCardControl card)
        {
            args.Cancel = true;
            return;
        }

        Guid gameId = card.ViewModel.Id;
        _draggedGameId = gameId;

        args.AllowedOperations =
            DataPackageOperation.Move;

        args.Data.RequestedOperation =
            DataPackageOperation.Move;

        args.Data.SetText(
            gameId.ToString("D"));
    }

    private void GameCard_DragOver(
        object sender,
        DragEventArgs args)
    {
        if (sender is not GameCardControl targetCard
            || _draggedGameId is not Guid sourceGameId
            || sourceGameId == targetCard.ViewModel.Id)
        {
            args.AcceptedOperation =
                DataPackageOperation.None;

            return;
        }

        args.AcceptedOperation =
            DataPackageOperation.Move;

        args.Handled = true;
    }

    private async void GameCard_Drop(
        object sender,
        DragEventArgs args)
    {
        if (sender is not GameCardControl targetCard
            || _draggedGameId is not Guid sourceGameId)
        {
            args.AcceptedOperation =
                DataPackageOperation.None;

            return;
        }

        Guid targetGameId = targetCard.ViewModel.Id;

        if (sourceGameId == targetGameId)
        {
            args.AcceptedOperation =
                DataPackageOperation.None;

            return;
        }

        args.AcceptedOperation =
            DataPackageOperation.Move;

        args.Handled = true;

        await ViewModel.ReorderGameAsync(
            sourceGameId,
            targetGameId);
    }

    private void GameCard_DropCompleted(
        UIElement sender,
        DropCompletedEventArgs args)
    {
        _draggedGameId = null;
    }

    private void OverviewItems_ElementPrepared(
        ItemsRepeater sender,
        ItemsRepeaterElementPreparedEventArgs args)
    {
        if (args.Element is GameCardControl card)
        {
            card.AppResourceService = _appResourceService;
        }
    }

    private void OverviewRoot_SizeChanged(
        object sender,
        SizeChangedEventArgs args)
    {
        _ = DispatcherQueue.TryEnqueue(UpdateOverviewLayout);
    }

    private void UpdateOverviewLayout()
    {
        double contentWidth = OverviewScrollViewer.ActualWidth;
        if (contentWidth <= 0d)
        {
            return;
        }

        int columns = OverviewLayoutPolicy.GetColumns(contentWidth);
        const double gap = 12d;
        double totalGap = gap * (columns - 1);
        OverviewItems.Width = contentWidth;
        OverviewUniformGridLayout.MaximumRowsOrColumns = columns;
        OverviewUniformGridLayout.MinItemWidth = Math.Max(
            1d,
            (contentWidth - totalGap) / columns);
        OverviewItems.InvalidateMeasure();
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
