using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using StaminaManager.Core.Validation;

namespace StaminaManager.Infrastructure.Windows;

internal sealed record AdjustableAcrylicState(
    bool IsInputActive,
    bool IsHighContrast,
    ElementTheme Theme);

internal interface IAdjustableAcrylicStateSource : IDisposable
{
    AdjustableAcrylicState Current { get; }

    event EventHandler? StateChanged;
}

internal interface IAdjustableAcrylicController : IDisposable
{
    float TintOpacity { get; set; }

    void ApplyState(AdjustableAcrylicState state);

    void ResetProperties();
}

internal sealed class AdjustableAcrylicLifecycle
{
    private readonly IAdjustableAcrylicController _controller;
    private readonly IAdjustableAcrylicStateSource _stateSource;
    private readonly Action _attachTarget;
    private readonly Action _detachTarget;
    private AdjustableAcrylicState? _lastState;
    private int _tintOpacityPercent =
        AcrylicOpacityPolicy.DefaultAcrylicTintOpacityPercent;
    private bool _isConnected;

    public AdjustableAcrylicLifecycle(
        IAdjustableAcrylicController controller,
        IAdjustableAcrylicStateSource stateSource,
        Action attachTarget,
        Action detachTarget)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(stateSource);
        ArgumentNullException.ThrowIfNull(attachTarget);
        ArgumentNullException.ThrowIfNull(detachTarget);
        _controller = controller;
        _stateSource = stateSource;
        _attachTarget = attachTarget;
        _detachTarget = detachTarget;
    }

    public void Connect()
    {
        if (_isConnected)
        {
            return;
        }

        _isConnected = true;
        _stateSource.StateChanged += OnStateChanged;
        try
        {
            ApplyState(_stateSource.Current, shouldResetProperties: false);
            _attachTarget();
            ApplyTintOpacity();
        }
        catch
        {
            Disconnect();
            throw;
        }
    }

    public void SetTintOpacityPercent(int tintOpacityPercent)
    {
        if (!AcrylicOpacityPolicy.IsValid(tintOpacityPercent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tintOpacityPercent));
        }

        _tintOpacityPercent = tintOpacityPercent;
        if (_isConnected)
        {
            ApplyTintOpacity();
        }
    }

    public void Disconnect()
    {
        if (!_isConnected)
        {
            return;
        }

        _isConnected = false;
        _stateSource.StateChanged -= OnStateChanged;
        try
        {
            _detachTarget();
        }
        finally
        {
            _stateSource.Dispose();
            _controller.Dispose();
            _lastState = null;
        }
    }

    private void OnStateChanged(object? sender, EventArgs args)
    {
        if (!_isConnected)
        {
            return;
        }

        AdjustableAcrylicState state = _stateSource.Current;
        bool shouldResetProperties = _lastState?.Theme != state.Theme;
        ApplyState(state, shouldResetProperties);
        if (shouldResetProperties)
        {
            ApplyTintOpacity();
        }
    }

    private void ApplyState(
        AdjustableAcrylicState state,
        bool shouldResetProperties)
    {
        _controller.ApplyState(state);
        _lastState = state;
        if (shouldResetProperties)
        {
            _controller.ResetProperties();
        }
    }

    private void ApplyTintOpacity() => _controller.TintOpacity =
        _tintOpacityPercent / 100f;
}

internal static class AdjustableAcrylicConnection
{
    public static AdjustableAcrylicLifecycle Connect(
        IAdjustableAcrylicController controller,
        Func<IAdjustableAcrylicStateSource> createStateSource,
        Action attachTarget,
        Action detachTarget,
        int tintOpacityPercent)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(createStateSource);
        IAdjustableAcrylicStateSource stateSource;
        try
        {
            stateSource = createStateSource();
        }
        catch
        {
            controller.Dispose();
            throw;
        }

        AdjustableAcrylicLifecycle lifecycle = new(
            controller,
            stateSource,
            attachTarget,
            detachTarget);
        try
        {
            lifecycle.SetTintOpacityPercent(tintOpacityPercent);
            lifecycle.Connect();
            return lifecycle;
        }
        catch
        {
            lifecycle.Disconnect();
            throw;
        }
    }
}

internal sealed class DesktopAcrylicControllerAdapter :
    IAdjustableAcrylicController
{
    private readonly DesktopAcrylicController _controller = new();
    private readonly ICompositionSupportsSystemBackdrop _target;
    private readonly SystemBackdropConfiguration _configuration = new();

    public DesktopAcrylicControllerAdapter(
        ICompositionSupportsSystemBackdrop target)
    {
        ArgumentNullException.ThrowIfNull(target);
        _target = target;
    }

    public float TintOpacity
    {
        get => _controller.TintOpacity;
        set => _controller.TintOpacity = value;
    }

    public void AttachTarget()
    {
        _controller.AddSystemBackdropTarget(_target);
        _controller.SetSystemBackdropConfiguration(_configuration);
    }

    public void DetachTarget() =>
        _controller.RemoveSystemBackdropTarget(_target);

    public void ApplyState(AdjustableAcrylicState state)
    {
        _configuration.IsInputActive = state.IsInputActive;
        _configuration.IsHighContrast = state.IsHighContrast;
        _configuration.Theme = state.Theme switch
        {
            ElementTheme.Light => SystemBackdropTheme.Light,
            ElementTheme.Dark => SystemBackdropTheme.Dark,
            _ => SystemBackdropTheme.Default,
        };
    }

    public void ResetProperties() => _controller.ResetProperties();

    public void Dispose() => _controller.Dispose();
}

/// <summary>
/// 色調不透明度を調整できるデスクトップAcrylicバックドロップ。
/// </summary>
public sealed class AdjustableAcrylicBackdrop : SystemBackdrop
{
    private AdjustableAcrylicLifecycle? _lifecycle;
    private int _tintOpacityPercent;

    public AdjustableAcrylicBackdrop(int tintOpacityPercent)
    {
        if (!AcrylicOpacityPolicy.IsValid(tintOpacityPercent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tintOpacityPercent));
        }

        _tintOpacityPercent = tintOpacityPercent;
    }

    public int TintOpacityPercent => _tintOpacityPercent;

    public void SetTintOpacityPercent(int tintOpacityPercent)
    {
        if (!AcrylicOpacityPolicy.IsValid(tintOpacityPercent))
        {
            throw new ArgumentOutOfRangeException(
                nameof(tintOpacityPercent));
        }

        _tintOpacityPercent = tintOpacityPercent;
        _lifecycle?.SetTintOpacityPercent(tintOpacityPercent);
    }

    protected override void OnTargetConnected(
        ICompositionSupportsSystemBackdrop connectedTarget,
        XamlRoot xamlRoot)
    {
        base.OnTargetConnected(connectedTarget, xamlRoot);
        _lifecycle?.Disconnect();
        _lifecycle = null;

        DesktopAcrylicControllerAdapter controller = new(connectedTarget);
        _lifecycle = AdjustableAcrylicConnection.Connect(
            controller,
            () => new XamlBackdropStateSource(
                connectedTarget,
                xamlRoot),
            controller.AttachTarget,
            controller.DetachTarget,
            _tintOpacityPercent);
    }

    protected override void OnTargetDisconnected(
        ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        try
        {
            _lifecycle?.Disconnect();
            _lifecycle = null;
        }
        finally
        {
            base.OnTargetDisconnected(disconnectedTarget);
        }
    }

    private sealed class XamlBackdropStateSource :
        IAdjustableAcrylicStateSource
    {
        private readonly FrameworkElement? _rootElement;
        private readonly Window? _window;
        private readonly ThemeSettings? _themeSettings;
        private readonly DispatcherQueue _dispatcherQueue;
        private bool _isInputActive = true;
        private bool _isDisposed;

        public XamlBackdropStateSource(
            ICompositionSupportsSystemBackdrop target,
            XamlRoot xamlRoot)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(xamlRoot);
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread()
                ?? throw new InvalidOperationException(
                    "UI DispatcherQueueを取得できませんでした。");
            _dispatcherQueue.EnsureSystemDispatcherQueue();
            _rootElement = xamlRoot.Content as FrameworkElement;
            _window = target as Window;
            if (_window is not null)
            {
                _themeSettings = ThemeSettings.CreateForWindowId(
                    _window.AppWindow.Id);
            }

            try
            {
                _rootElement?.ActualThemeChanged += OnActualThemeChanged;
                _window?.Activated += OnWindowActivated;
                if (_themeSettings is not null)
                {
                    _themeSettings.Changed += OnThemeSettingsChanged;
                }
            }
            catch
            {
                if (_rootElement is not null)
                {
                    _rootElement.ActualThemeChanged -= OnActualThemeChanged;
                }

                if (_window is not null)
                {
                    _window.Activated -= OnWindowActivated;
                }

                if (_themeSettings is not null)
                {
                    _themeSettings.Changed -= OnThemeSettingsChanged;
                }

                throw;
            }
        }

        public AdjustableAcrylicState Current => new(
            _isInputActive,
            _themeSettings?.HighContrast ?? false,
            _rootElement?.ActualTheme ?? ElementTheme.Default);

        public event EventHandler? StateChanged;

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            if (_rootElement is not null)
            {
                _rootElement.ActualThemeChanged -= OnActualThemeChanged;
            }

            if (_window is not null)
            {
                _window.Activated -= OnWindowActivated;
            }

            if (_themeSettings is not null)
            {
                _themeSettings.Changed -= OnThemeSettingsChanged;
            }
        }

        private void OnActualThemeChanged(
            FrameworkElement sender,
            object args) => QueueStateChanged();

        private void OnWindowActivated(
            object sender,
            WindowActivatedEventArgs args)
        {
            _isInputActive = args.WindowActivationState
                != WindowActivationState.Deactivated;
            QueueStateChanged();
        }

        private void OnThemeSettingsChanged(
            ThemeSettings sender,
            object args) => QueueStateChanged();

        private void QueueStateChanged()
        {
            if (!_isDisposed)
            {
                _dispatcherQueue.TryEnqueue(
                    () => StateChanged?.Invoke(this, EventArgs.Empty));
            }
        }
    }
}
