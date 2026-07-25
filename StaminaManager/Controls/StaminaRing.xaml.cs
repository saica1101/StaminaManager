using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using StaminaManager.Core.Calculations;
using Windows.Foundation;

namespace StaminaManager.Controls;

public sealed partial class StaminaRing : UserControl
{
    private const double StrokeThickness = 8d;
    private const double StartDegrees = -90d;
    private bool _isInitialized;

    public static readonly DependencyProperty CurrentProperty =
        DependencyProperty.Register(
            nameof(Current),
            typeof(int),
            typeof(StaminaRing),
            new PropertyMetadata(0, OnVisualPropertyChanged));

    public static readonly DependencyProperty MaximumProperty =
        DependencyProperty.Register(
            nameof(Maximum),
            typeof(int),
            typeof(StaminaRing),
            new PropertyMetadata(1, OnVisualPropertyChanged));

    public static readonly DependencyProperty RatioProperty =
        DependencyProperty.Register(
            nameof(Ratio),
            typeof(double),
            typeof(StaminaRing),
            new PropertyMetadata(0d, OnVisualPropertyChanged));

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(
            nameof(StatusText),
            typeof(string),
            typeof(StaminaRing),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public static readonly DependencyProperty StatusBrushProperty =
        DependencyProperty.Register(
            nameof(StatusBrush),
            typeof(Brush),
            typeof(StaminaRing),
            new PropertyMetadata(null, OnVisualPropertyChanged));

    public static readonly DependencyProperty GameNameProperty =
        DependencyProperty.Register(
            nameof(GameName),
            typeof(string),
            typeof(StaminaRing),
            new PropertyMetadata(string.Empty, OnVisualPropertyChanged));

    public StaminaRing()
    {
        InitializeComponent();
        _isInitialized = true;
        UpdateVisual();
    }

    public int Current
    {
        get => (int)GetValue(CurrentProperty);
        set => SetValue(CurrentProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Ratio
    {
        get => (double)GetValue(RatioProperty);
        set => SetValue(RatioProperty, value);
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public Brush? StatusBrush
    {
        get => (Brush?)GetValue(StatusBrushProperty);
        set => SetValue(StatusBrushProperty, value);
    }

    public string GameName
    {
        get => (string)GetValue(GameNameProperty);
        set => SetValue(GameNameProperty, value);
    }

    private static void OnVisualPropertyChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is StaminaRing ring)
        {
            ring.UpdateVisual();
        }
    }

    private void StaminaRing_SizeChanged(
        object sender,
        SizeChangedEventArgs args) => UpdateVisual();

    private void UpdateVisual()
    {
        if (!_isInitialized)
        {
            return;
        }

        UpdateAutomationName();
        double diameter = Math.Min(ActualWidth, ActualHeight);
        double radius = (diameter - StrokeThickness) / 2d;
        double sweepDegrees = StaminaRingMath.GetSweepDegrees(Ratio);
        if (radius <= 0d || sweepDegrees <= 0d)
        {
            ProgressPath.Data = null;
            return;
        }

        double centerX = ActualWidth / 2d;
        double centerY = ActualHeight / 2d;
        double endRadians = (StartDegrees + sweepDegrees)
            * Math.PI
            / 180d;
        Point startPoint = new(centerX, centerY - radius);
        Point endPoint = new(
            centerX + radius * Math.Cos(endRadians),
            centerY + radius * Math.Sin(endRadians));
        ArcSegment arc = new()
        {
            Point = endPoint,
            Size = new Size(radius, radius),
            IsLargeArc = sweepDegrees > 180d,
            SweepDirection = SweepDirection.Clockwise,
        };
        PathFigure figure = new()
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false,
        };
        figure.Segments.Add(arc);
        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        ProgressPath.Data = geometry;
    }

    private void UpdateAutomationName()
    {
        string name = string.Join(
            ", ",
            GameName,
            $"{Current} / {Maximum}",
            StatusText);
        AutomationProperties.SetName(this, name);
    }
}
