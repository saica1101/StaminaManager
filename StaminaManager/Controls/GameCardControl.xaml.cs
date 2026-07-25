using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Windows.ApplicationModel.Resources;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;
using StaminaManager.ViewModels;
using System.ComponentModel;
using System.Globalization;

namespace StaminaManager.Controls;

public sealed partial class GameCardControl : UserControl, INotifyPropertyChanged
{
    private const string DataDirectoryName = "Data";
    private const string AssetsDirectoryName = "Assets";
    private readonly ResourceLoader _resources = new();
    private Brush? _statusBrush;
    private string? _imageAssetId;
    private int _imageAttempt;
    private GameCardViewModel? _subscribedViewModel;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(GameCardViewModel),
            typeof(GameCardControl),
            new PropertyMetadata(null, OnViewModelChanged));

    public GameCardControl()
    {
        InitializeComponent();
        ActualThemeChanged += GameCardControl_ActualThemeChanged;
        Loaded += GameCardControl_Loaded;
        Unloaded += GameCardControl_Unloaded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GameCardViewModel ViewModel
    {
        get => (GameCardViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public string CardAutomationId =>
        CurrentViewModel is { } viewModel
            ? $"GameCard_{viewModel.Id:D}"
            : string.Empty;

    public string StatusText => CurrentViewModel is { } viewModel
        ? GetRequiredString(
            viewModel.Status switch
            {
                StaminaStatus.Safe => "StaminaStatusSafe",
                StaminaStatus.Attention => "StaminaStatusAttention",
                StaminaStatus.NearFull => "StaminaStatusNearFull",
                StaminaStatus.Full => "StaminaStatusFull",
                StaminaStatus.OverCap => "StaminaStatusOverCap",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(viewModel.Status)),
            })
        : string.Empty;

    public Brush? StatusBrush => _statusBrush;

    public ImageSource? GameImageSource { get; private set; }

    public Visibility GameImageVisibility => GameImageSource is null
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility FallbackVisibility => GameImageSource is null
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string RemainingText
    {
        get
        {
            if (CurrentViewModel is not { } viewModel)
            {
                return string.Empty;
            }

            RemainingTimeParts parts = RemainingTimeParts.From(
                viewModel.Remaining);
            if (parts.IsFull)
            {
                return GetRequiredString("RemainingTimeFull");
            }

            string resourceId = parts.Days > 0
                ? "RemainingTimeDaysFormat"
                : "RemainingTimeHoursFormat";
            object[] values = parts.Days > 0
                ? [parts.Days, parts.Hours, parts.Minutes]
                : [parts.Hours, parts.Minutes];
            return string.Format(
                CultureInfo.CurrentCulture,
                GetRequiredString(resourceId),
                values);
        }
    }

    public string CardAutomationName => CurrentViewModel is { } viewModel
        ? string.Format(
            CultureInfo.CurrentCulture,
            GetRequiredString("GameCardAutomationNameFormat"),
            viewModel.Name,
            viewModel.CurrentStamina,
            viewModel.MaxStamina,
            StatusText,
            RemainingText)
        : string.Empty;

    private GameCardViewModel? CurrentViewModel =>
        GetValue(ViewModelProperty) as GameCardViewModel;

    private static void OnViewModelChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        GameCardControl control = (GameCardControl)dependencyObject;
        control.SubscribeToViewModel(args.NewValue as GameCardViewModel);
        control.UpdatePresentation(shouldUpdateImage: true);
    }

    private void GameCardControl_Loaded(
        object sender,
        RoutedEventArgs args)
    {
        SubscribeToViewModel(CurrentViewModel);
        UpdatePresentation(shouldUpdateImage: true);
    }

    private void GameCardControl_Unloaded(
        object sender,
        RoutedEventArgs args) => SubscribeToViewModel(null);

    private void SubscribeToViewModel(GameCardViewModel? viewModel)
    {
        if (ReferenceEquals(_subscribedViewModel, viewModel))
        {
            return;
        }

        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged -=
                OnViewModelPropertyChanged;
        }

        _subscribedViewModel = viewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PropertyChanged +=
                OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(
        object? sender,
        PropertyChangedEventArgs args) => UpdatePresentation(
            shouldUpdateImage:
                args.PropertyName == nameof(ViewModel.ImageAssetId));

    private void GameCardControl_ActualThemeChanged(
        FrameworkElement sender,
        object args) => UpdatePresentation(shouldUpdateImage: false);

    private void UpdatePresentation(bool shouldUpdateImage)
    {
        if (CurrentViewModel is not { } viewModel)
        {
            return;
        }

        _statusBrush = ResolveStatusBrush(viewModel.Status);
        if (shouldUpdateImage)
        {
            _imageAssetId = NormalizeAssetId(viewModel.ImageAssetId);
            _imageAttempt = 0;
            GameImageSource = CreateOwnedImageSource(
                _imageAssetId,
                ".png");
        }

        NotifyDerivedProperties();
        Bindings.Update();
    }

    private Brush ResolveStatusBrush(StaminaStatus status)
    {
        string resourceKey = status switch
        {
            StaminaStatus.Safe => "SafeBrush",
            StaminaStatus.Attention => "AttentionBrush",
            StaminaStatus.NearFull
                or StaminaStatus.Full
                or StaminaStatus.OverCap => "UrgentBrush",
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        return (Brush)Microsoft.UI.Xaml.Application.Current.Resources[
            resourceKey];
    }

    private static string? NormalizeAssetId(string? assetId)
    {
        if (string.IsNullOrWhiteSpace(assetId)
            || !Guid.TryParseExact(assetId, "N", out Guid parsed)
            || !string.Equals(
                assetId,
                parsed.ToString("N"),
                StringComparison.Ordinal))
        {
            return null;
        }

        return assetId;
    }

    private static ImageSource? CreateOwnedImageSource(
        string? assetId,
        string extension)
    {
        return assetId is null
            ? null
            : new BitmapImage(new Uri(
                $"ms-appdata:///local/{DataDirectoryName}/"
                + $"{AssetsDirectoryName}/{assetId}{extension}"));
    }

    private void GameImage_ImageFailed(
        object sender,
        ExceptionRoutedEventArgs args)
    {
        if (_imageAssetId is not null && _imageAttempt == 0)
        {
            _imageAttempt = 1;
            GameImageSource = CreateOwnedImageSource(
                _imageAssetId,
                ".jpg");
        }
        else
        {
            _imageAssetId = null;
            GameImageSource = null;
        }

        NotifyDerivedProperties();
        Bindings.Update();
    }

    private void NotifyDerivedProperties()
    {
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(CardAutomationId)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(CardAutomationName)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(StatusText)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(StatusBrush)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(RemainingText)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(GameImageSource)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(GameImageVisibility)));
        PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(FallbackVisibility)));
    }

    private string GetRequiredString(string resourceId)
    {
        string value = _resources.GetString(resourceId);
        return string.IsNullOrWhiteSpace(value) ? resourceId : value;
    }
}
