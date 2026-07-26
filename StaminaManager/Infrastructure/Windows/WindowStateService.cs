using Microsoft.UI.Windowing;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;
using System.Diagnostics;
using System.Globalization;
using Windows.Graphics;
using Windows.Storage;
using WinUIEx;

namespace StaminaManager.Infrastructure.Windows;

public sealed class WindowStateService : IWindowStateService
{
    private const double EffectivePixelDpi = 96d;
    private readonly IWindowStateAdapter _adapter;
    private readonly IWindowSnapshotStore _snapshotStore;
    private readonly Action<Type> _reportFailure;
    private WindowBounds _standardRestoredBounds;
    private bool _isInitialized;
    private bool _isTransitioning;

    public WindowStateService(Func<MainWindow?> getWindow)
        : this(
            new MainWindowStateAdapter(getWindow),
            new ApplicationDataWindowSnapshotStore(),
            ReportFailure)
    {
    }

    internal WindowStateService(
        IWindowStateAdapter adapter,
        IWindowSnapshotStore snapshotStore,
        Action<Type> reportFailure)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(snapshotStore);
        ArgumentNullException.ThrowIfNull(reportFailure);
        _adapter = adapter;
        _snapshotStore = snapshotStore;
        _reportFailure = reportFailure;
        _adapter.BoundsChanged += OnBoundsChanged;
    }

    public AppDisplayMode CurrentDisplayMode { get; private set; } =
        AppDisplayMode.Standard;

    public void ApplyDisplayMode(AppDisplayMode displayMode)
    {
        if (_isInitialized && CurrentDisplayMode == displayMode)
        {
            return;
        }

        _isTransitioning = true;
        try
        {
            if (_isInitialized)
            {
                SaveSnapshot(CurrentDisplayMode, CaptureSnapshot());
            }

            CurrentDisplayMode = displayMode;
            WindowBoundsProfile profile = WindowBoundsPolicy.GetProfile(
                displayMode);
            _adapter.SetPersistenceId(profile.PersistenceId);
            _adapter.SetMinimumSize(profile.MinimumSize);
            if (_adapter.PresenterState != WindowPresenterState.Restored)
            {
                _adapter.Restore();
            }

            WindowBoundsSnapshot? saved = LoadSnapshot(displayMode);
            WindowBoundsSnapshot source = saved ?? CreateInitialSnapshot(
                profile);
            WindowWorkArea workArea = _adapter.GetNearestWorkArea(
                source.Bounds);
            WindowBounds restored = WindowBoundsPolicy.Restore(
                displayMode,
                source,
                workArea,
                _adapter.CurrentDpi);
            _adapter.MoveAndResize(restored);
            if (displayMode == AppDisplayMode.Standard)
            {
                _standardRestoredBounds = restored;
                if (saved?.IsMaximized == true)
                {
                    _adapter.Maximize();
                }
            }

            _isInitialized = true;
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    public void CaptureCurrent()
    {
        if (!_isInitialized)
        {
            return;
        }

        SaveSnapshot(CurrentDisplayMode, CaptureSnapshot());
    }

    private WindowBoundsSnapshot CreateInitialSnapshot(
        WindowBoundsProfile profile)
    {
        WindowBounds current = _adapter.CurrentBounds;
        return new WindowBoundsSnapshot(
            current with
            {
                Width = profile.InitialSize.Width,
                Height = profile.InitialSize.Height,
            },
            EffectivePixelDpi);
    }

    private WindowBoundsSnapshot CaptureSnapshot()
    {
        WindowPresenterState presenterState = _adapter.PresenterState;
        WindowBounds bounds = _adapter.CurrentBounds;
        if (CurrentDisplayMode == AppDisplayMode.Standard
            && presenterState != WindowPresenterState.Restored)
        {
            bounds = _standardRestoredBounds;
        }

        return new WindowBoundsSnapshot(
            bounds,
            _adapter.CurrentDpi,
            IsMaximized: CurrentDisplayMode == AppDisplayMode.Standard
                && presenterState == WindowPresenterState.Maximized);
    }

    private WindowBoundsSnapshot? LoadSnapshot(AppDisplayMode displayMode)
    {
        try
        {
            return _snapshotStore.Load(displayMode);
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            _reportFailure(exception.GetType());
            return null;
        }
    }

    private void SaveSnapshot(
        AppDisplayMode displayMode,
        WindowBoundsSnapshot snapshot)
    {
        try
        {
            _snapshotStore.Save(displayMode, snapshot);
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            _reportFailure(exception.GetType());
        }
    }

    private void OnBoundsChanged(object? sender, EventArgs args)
    {
        if (_isTransitioning
            || CurrentDisplayMode != AppDisplayMode.Standard
            || _adapter.PresenterState != WindowPresenterState.Restored)
        {
            return;
        }

        _standardRestoredBounds = _adapter.CurrentBounds;
    }

    private static bool IsProcessFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;

    private static void ReportFailure(Type exceptionType) =>
        Debug.WriteLine(
            "Window state persistence failed: " + exceptionType.Name);
}

internal interface IWindowStateAdapter
{
    event EventHandler? BoundsChanged;

    WindowBounds CurrentBounds { get; }

    double CurrentDpi { get; }

    WindowPresenterState PresenterState { get; }

    WindowWorkArea GetNearestWorkArea(WindowBounds bounds);

    void SetPersistenceId(string persistenceId);

    void SetMinimumSize(WindowSize minimumSize);

    void MoveAndResize(WindowBounds bounds);

    void Restore();

    void Maximize();
}

internal interface IWindowSnapshotStore
{
    WindowBoundsSnapshot? Load(AppDisplayMode displayMode);

    void Save(
        AppDisplayMode displayMode,
        WindowBoundsSnapshot snapshot);
}

internal sealed class MainWindowStateAdapter : IWindowStateAdapter
{
    private readonly Func<MainWindow?> _getWindow;
    private MainWindow? _subscribedWindow;

    public MainWindowStateAdapter(Func<MainWindow?> getWindow)
    {
        ArgumentNullException.ThrowIfNull(getWindow);
        _getWindow = getWindow;
    }

    public event EventHandler? BoundsChanged;

    public WindowBounds CurrentBounds
    {
        get
        {
            AppWindow appWindow = GetWindow().AppWindow;
            return new WindowBounds(
                appWindow.Position.X,
                appWindow.Position.Y,
                appWindow.Size.Width,
                appWindow.Size.Height);
        }
    }

    public double CurrentDpi => GetWindow().GetDpiForWindow();

    public WindowPresenterState PresenterState =>
        GetWindow().AppWindow.Presenter is OverlappedPresenter presenter
            ? presenter.State switch
            {
                OverlappedPresenterState.Maximized =>
                    WindowPresenterState.Maximized,
                OverlappedPresenterState.Minimized =>
                    WindowPresenterState.Minimized,
                _ => WindowPresenterState.Restored,
            }
            : WindowPresenterState.Restored;

    public WindowWorkArea GetNearestWorkArea(WindowBounds bounds)
    {
        DisplayArea displayArea = DisplayArea.GetFromRect(
            new RectInt32(bounds.X, bounds.Y, bounds.Width, bounds.Height),
            DisplayAreaFallback.Nearest);
        RectInt32 workArea = displayArea.WorkArea;
        return new WindowWorkArea(
            workArea.X,
            workArea.Y,
            workArea.Width,
            workArea.Height);
    }

    public void SetPersistenceId(string persistenceId) =>
        WindowManager.Get(GetWindow()).PersistenceId = persistenceId;

    public void SetMinimumSize(WindowSize minimumSize)
    {
        WindowManager manager = WindowManager.Get(GetWindow());
        manager.MinWidth = minimumSize.Width;
        manager.MinHeight = minimumSize.Height;
    }

    public void MoveAndResize(WindowBounds bounds) =>
        GetWindow().AppWindow.MoveAndResize(new RectInt32(
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height));

    public void Restore()
    {
        if (GetWindow().AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Restore();
        }
    }

    public void Maximize()
    {
        if (GetWindow().AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Maximize();
        }
    }

    private MainWindow GetWindow()
    {
        MainWindow window = _getWindow()
            ?? throw new InvalidOperationException(
                "MainWindowがまだ作成されていません。");
        if (!ReferenceEquals(window, _subscribedWindow))
        {
            window.AppWindow.Changed += OnAppWindowChanged;
            _subscribedWindow = window;
        }

        return window;
    }

    private void OnAppWindowChanged(
        AppWindow sender,
        AppWindowChangedEventArgs args)
    {
        if (args.DidPositionChange
            || args.DidSizeChange
            || args.DidPresenterChange)
        {
            BoundsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}

internal sealed class ApplicationDataWindowSnapshotStore
    : IWindowSnapshotStore
{
    private const string KeyPrefix = "StaminaManager.WindowState.";

    public WindowBoundsSnapshot? Load(AppDisplayMode displayMode)
    {
        string key = KeyPrefix + displayMode;
        if (ApplicationData.Current.LocalSettings.Values[key]
            is not string value)
        {
            return null;
        }

        string[] parts = value.Split('|');
        if (parts.Length != 6
            || !int.TryParse(parts[0], CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(parts[1], CultureInfo.InvariantCulture, out int y)
            || !int.TryParse(
                parts[2],
                CultureInfo.InvariantCulture,
                out int width)
            || !int.TryParse(
                parts[3],
                CultureInfo.InvariantCulture,
                out int height)
            || !double.TryParse(
                parts[4],
                CultureInfo.InvariantCulture,
                out double dpi)
            || !bool.TryParse(parts[5], out bool isMaximized))
        {
            throw new InvalidDataException(
                "保存済みウィンドウ状態の形式が正しくありません。");
        }

        return new WindowBoundsSnapshot(
            new WindowBounds(x, y, width, height),
            dpi,
            isMaximized);
    }

    public void Save(
        AppDisplayMode displayMode,
        WindowBoundsSnapshot snapshot)
    {
        string key = KeyPrefix + displayMode;
        ApplicationData.Current.LocalSettings.Values[key] = string.Join(
            '|',
            snapshot.Bounds.X.ToString(CultureInfo.InvariantCulture),
            snapshot.Bounds.Y.ToString(CultureInfo.InvariantCulture),
            snapshot.Bounds.Width.ToString(CultureInfo.InvariantCulture),
            snapshot.Bounds.Height.ToString(CultureInfo.InvariantCulture),
            snapshot.SavedDpi.ToString(CultureInfo.InvariantCulture),
            snapshot.IsMaximized.ToString(CultureInfo.InvariantCulture));
    }
}
