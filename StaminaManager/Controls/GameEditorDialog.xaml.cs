using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StaminaManager.Application;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Storage;
using StaminaManager.ViewModels;
using System.ComponentModel;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace StaminaManager.Controls;

public enum GameEditorDialogResult
{
    Canceled,
    Saved,
    Deleted,
}

public sealed partial class GameEditorDialog : ContentDialog
{
    private readonly AssetStore _assetStore;
    private readonly GameManager _gameManager;
    private readonly nint _ownerWindowHandle;
    private GameEditorDialogResult _result;
    private bool _focusCurrentStamina;

    public GameEditorDialog(
        GameEditorViewModel viewModel,
        AssetStore assetStore,
        GameManager gameManager,
        nint ownerWindowHandle)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        ArgumentNullException.ThrowIfNull(assetStore);
        ArgumentNullException.ThrowIfNull(gameManager);
        ViewModel = viewModel;
        _assetStore = assetStore;
        _gameManager = gameManager;
        _ownerWindowHandle = ownerWindowHandle;
        InitializeComponent();
        Opened += OnOpened;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateDialogActions();
    }

    public GameEditorViewModel ViewModel { get; }

    public async Task<GameEditorDialogResult> ShowAsync(
        XamlRoot xamlRoot,
        bool focusCurrentStamina,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        cancellationToken.ThrowIfCancellationRequested();
        XamlRoot = xamlRoot;
        _focusCurrentStamina = focusCurrentStamina;
        _result = GameEditorDialogResult.Canceled;
        using CancellationTokenRegistration registration =
            cancellationToken.Register(
                () => DispatcherQueue.TryEnqueue(Hide));
        _ = await base.ShowAsync();
        await CleanupAssetsAsync(CancellationToken.None);
        return _result;
    }

    public static Visibility BoolToVisibility(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility TextToVisibility(string? value) =>
        HasText(value) ? Visibility.Visible : Visibility.Collapsed;

    public static bool HasText(string? value) =>
        !string.IsNullOrWhiteSpace(value);

    public static string? FirstNonEmptyError(
        string? fieldError,
        string? intervalError) =>
        HasText(fieldError) ? fieldError : intervalError;

    public static Visibility BoolToVisibility(
        GameEditorState value,
        GameEditorState expected) =>
        value == expected ? Visibility.Visible : Visibility.Collapsed;

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        UpdateGeneratedButtonAutomation();
        Control target = _focusCurrentStamina
            ? CurrentStaminaInput
            : GameNameInput;
        target.Focus(FocusState.Programmatic);
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(GameEditorViewModel.State)
            && ViewModel.State == GameEditorState.DeleteConfirmation)
        {
            UpdateDialogActions();
            _ = DispatcherQueue.TryEnqueue(
                () => FocusDialogAction("DeleteBackButton"));
        }
        else if (args.PropertyName == nameof(GameEditorViewModel.State))
        {
            UpdateDialogActions();
            GameNameInput.Focus(FocusState.Programmatic);
        }
    }

    private async void ChooseGameImageButton_Click(
        object sender,
        RoutedEventArgs args)
    {
        try
        {
            FileOpenPicker picker = new();
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                _ownerWindowHandle);
            StorageFile? file = await picker.PickSingleFileAsync();
            if (file is null)
            {
                return;
            }

            await using Stream source = await file.OpenStreamForReadAsync();
            StoredAsset stored = await _assetStore.SaveAsync(
                source,
                file.Name,
                CancellationToken.None);
            ViewModel.ImageAssetId = stored.AssetId;
            SelectedImageText.Text = file.Name;
        }
        catch (Exception exception) when (
            exception is AssetValidationException
                or IOException
                or UnauthorizedAccessException)
        {
            ViewModel.ShowGeneralError(
                exception is AssetValidationException
                    ? "画像を使用できません。PNG/JPEG、5MB以下、4096×4096以下のファイルを選んでください。"
                    : "画像を読み込めませんでした。別のファイルを選んでください。");
        }
    }

    private async void Dialog_PrimaryButtonClick(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        ContentDialogButtonClickDeferral deferral = args.GetDeferral();
        args.Cancel = true;
        try
        {
            if (ViewModel.State == GameEditorState.DeleteConfirmation)
            {
                if (!await ViewModel.DeleteAsync(CancellationToken.None))
                {
                    ViewModel.ShowGeneralError(
                        "ゲームは既に削除されています。"
                        + "画面を閉じて一覧を確認してください。");
                    return;
                }

                _result = GameEditorDialogResult.Deleted;
                args.Cancel = false;
                return;
            }

            GameEntry? saved = await ViewModel.SaveAsync(
                CancellationToken.None);
            if (saved is null)
            {
                return;
            }

            _result = GameEditorDialogResult.Saved;
            args.Cancel = false;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException)
        {
            // ViewModel が安全な表示文へ変換する。入力内容は保持する。
        }
        finally
        {
            deferral.Complete();
        }
    }

    private void Dialog_CloseButtonClick(
        ContentDialog sender,
        ContentDialogButtonClickEventArgs args)
    {
        if (ViewModel.State == GameEditorState.DeleteConfirmation)
        {
            args.Cancel = true;
            ViewModel.BackToEditingCommand.Execute(null);
            return;
        }

        _result = GameEditorDialogResult.Canceled;
    }

    private void UpdateDialogActions()
    {
        bool isDeleteConfirmation =
            ViewModel.State == GameEditorState.DeleteConfirmation;
        PrimaryButtonStyle = (Style)Resources[
            isDeleteConfirmation
                ? "DeleteConfirmButtonStyle"
                : "GameEditorSaveButtonStyle"];
        CloseButtonStyle = (Style)Resources[
            isDeleteConfirmation
                ? "DeleteBackButtonStyle"
                : "GameEditorCancelButtonStyle"];
        DefaultButton = isDeleteConfirmation
            ? ContentDialogButton.Close
            : ContentDialogButton.Primary;
        UpdateGeneratedButtonAutomation();
    }

    private void UpdateGeneratedButtonAutomation()
    {
        bool isDeleteConfirmation =
            ViewModel.State == GameEditorState.DeleteConfirmation;
        if (GetTemplateChild("PrimaryButton") is Button primaryButton)
        {
            AutomationProperties.SetAutomationId(
                primaryButton,
                isDeleteConfirmation
                    ? "DeleteConfirmButton"
                    : "GameEditorSaveButton");
            AutomationProperties.SetName(
                primaryButton,
                isDeleteConfirmation
                    ? "このゲームを削除する"
                    : ViewModel.IsNew
                        ? "ゲームを追加する"
                        : "ゲームの変更を保存する");
        }

        if (GetTemplateChild("CloseButton") is Button closeButton)
        {
            AutomationProperties.SetAutomationId(
                closeButton,
                isDeleteConfirmation
                    ? "DeleteBackButton"
                    : "GameEditorCancelButton");
            AutomationProperties.SetName(
                closeButton,
                isDeleteConfirmation
                    ? "削除せず編集へ戻る"
                    : "編集をキャンセル");
        }
    }

    private void FocusDialogAction(string automationId)
    {
        if (FindDescendantByAutomationId(this, automationId)
            is Control control)
        {
            control.Focus(FocusState.Programmatic);
            return;
        }

        Focus(FocusState.Programmatic);
    }

    private static DependencyObject? FindDescendantByAutomationId(
        DependencyObject root,
        string automationId)
    {
        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is FrameworkElement element
                && string.Equals(
                    AutomationProperties.GetAutomationId(element),
                    automationId,
                    StringComparison.Ordinal))
            {
                return child;
            }

            DependencyObject? descendant = FindDescendantByAutomationId(
                child,
                automationId);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private async Task CleanupAssetsAsync(
        CancellationToken cancellationToken)
    {
        HashSet<string> referencedAssets = _gameManager.Games
            .Select(game => game.ImageAssetId)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        try
        {
            await _assetStore.DeleteOrphansAfterCommitAsync(
                referencedAssets,
                cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine(
                "未参照画像の整理に失敗しました: "
                + exception.GetType().Name);
        }
    }
}
