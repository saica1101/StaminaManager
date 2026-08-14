using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Dispatching;
using StaminaManager.Core.Validation;
using System.Diagnostics;

namespace StaminaManager.Infrastructure.Windows;

internal interface IAdjustableAcrylicController : IDisposable
{
    float TintOpacity { get; set; }

    float LuminosityOpacity { get; set; }

    void ResetProperties();
}

internal sealed class AdjustableAcrylicLifecycle
{
    private readonly IAdjustableAcrylicController _controller;
    private readonly Action _attachTarget;
    private readonly Action _detachTarget;
    private int _tintOpacityPercent =
        AcrylicOpacityPolicy.DefaultAcrylicTintOpacityPercent;
    private bool _isConnected;
    private bool _isDisposed;

    internal bool IsConnected => _isConnected;

    public AdjustableAcrylicLifecycle(
        IAdjustableAcrylicController controller,
        Action attachTarget,
        Action detachTarget)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(attachTarget);
        ArgumentNullException.ThrowIfNull(detachTarget);
        _controller = controller;
        _attachTarget = attachTarget;
        _detachTarget = detachTarget;
    }

    public void Connect()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_isConnected)
        {
            return;
        }

        _isConnected = true;
        try
        {
            _attachTarget();
            ApplyOpacity();
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
            ApplyOpacity();
        }
    }

    public void Disconnect()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        bool wasConnected = _isConnected;
        _isConnected = false;
        try
        {
            if (wasConnected)
            {
                _detachTarget();
            }
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Debug.WriteLine(
                "Acrylic backdrop target detach failed: "
                + exception.GetType().Name);
        }
        finally
        {
            _controller.Dispose();
        }
    }

    public void OnDefaultSystemBackdropConfigurationChanged()
    {
        if (!_isConnected)
        {
            return;
        }

        try
        {
            // Opacityを設定するとControllerの自動テーマ追従が無効になるため、
            // WinUIが既定構成を更新した後に色をシステム既定へ戻して再適用する。
            _controller.ResetProperties();
            ApplyOpacity();
        }
        catch (Exception exception) when (!IsProcessFatal(exception))
        {
            Disconnect();
            Debug.WriteLine(
                "Acrylic backdrop configuration update failed: "
                + exception.GetType().Name);
        }
    }

    private void ApplyOpacity()
    {
        float opacity = _tintOpacityPercent / 100f;
        _controller.TintOpacity = opacity;
        _controller.LuminosityOpacity = opacity;
    }

    private static bool IsProcessFatal(Exception exception) =>
        exception is OutOfMemoryException
            or StackOverflowException
            or AccessViolationException
            or AppDomainUnloadedException
            or BadImageFormatException
            or CannotUnloadAppDomainException
            or InvalidProgramException;
}

internal static class AdjustableAcrylicConnection
{
    public static AdjustableAcrylicLifecycle Connect(
        IAdjustableAcrylicController controller,
        Action attachTarget,
        Action detachTarget,
        int tintOpacityPercent)
    {
        ArgumentNullException.ThrowIfNull(controller);
        AdjustableAcrylicLifecycle lifecycle = new(
            controller,
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
    private readonly SystemBackdropConfiguration _configuration;
    private bool _isTargetAttached;

    public DesktopAcrylicControllerAdapter(
        ICompositionSupportsSystemBackdrop target,
        SystemBackdropConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(configuration);

        _target = target;
        _configuration = configuration;
    }

    public float TintOpacity
    {
        get => _controller.TintOpacity;
        set => _controller.TintOpacity = value;
    }

    public float LuminosityOpacity
    {
        get => _controller.LuminosityOpacity;
        set => _controller.LuminosityOpacity = value;
    }

    public void AttachTarget()
    {
        // 先にWinUIが管理するConfigurationをControllerへ渡す。
        // Configurationには現在のTheme / IsInputActive /
        // IsHighContrastが反映される。
        _controller.SetSystemBackdropConfiguration(_configuration);

        if (!_controller.AddSystemBackdropTarget(_target))
        {
            throw new InvalidOperationException(
                "Desktop Acrylicのバックドロップターゲットを接続できませんでした。");
        }

        _isTargetAttached = true;
    }

    public void DetachTarget()
    {
        if (!_isTargetAttached)
        {
            return;
        }

        try
        {
            _controller.RemoveSystemBackdropTarget(_target);
        }
        finally
        {
            _isTargetAttached = false;
        }
    }

    public void ResetProperties()
    {
        // TintOpacity / LuminosityOpacityを変更すると、
        // Controllerの自動Light/Dark切替が無効になる。
        //
        // ResetPropertiesで現在のSystemBackdropConfigurationに
        // 対応したシステム既定のAcrylic外観へ戻す。
        //
        // この直後にLifecycle側でユーザー指定Opacityだけを
        // 再適用する。
        _controller.ResetProperties();
    }

    public void Dispose()
    {
        _controller.Dispose();
    }
}

/// <summary>
/// 色調不透明度を調整できるデスクトップAcrylicバックドロップ。
/// </summary>
public sealed class AdjustableAcrylicBackdrop : SystemBackdrop
{
    private AdjustableAcrylicLifecycle? _lifecycle;
    private int _tintOpacityPercent;
    private bool _configurationUpdateQueued;

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

        SystemBackdropConfiguration configuration =
            GetDefaultSystemBackdropConfiguration(
                connectedTarget,
                xamlRoot);
        DesktopAcrylicControllerAdapter controller = new(
            connectedTarget,
            configuration);
        _lifecycle = AdjustableAcrylicConnection.Connect(
            controller,
            controller.AttachTarget,
            controller.DetachTarget,
            _tintOpacityPercent);
            
        QueueBackdropConfigurationUpdate();
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

    protected override void OnDefaultSystemBackdropConfigurationChanged(
        ICompositionSupportsSystemBackdrop target,
        XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        QueueBackdropConfigurationUpdate();
    }

    private void QueueBackdropConfigurationUpdate()
    {
        if (_configurationUpdateQueued)
        {
            return;
        }

        _configurationUpdateQueued = true;

        DispatcherQueue? dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        if (dispatcherQueue is null)
        {
            _configurationUpdateQueued = false;
            ApplyBackdropConfigurationUpdate();
            return;
        }

        bool queued = dispatcherQueue.TryEnqueue(() =>
        {
            _configurationUpdateQueued = false;
            ApplyBackdropConfigurationUpdate();
        });

        if (!queued)
        {
            _configurationUpdateQueued = false;
            ApplyBackdropConfigurationUpdate();
        }
    }

    private void ApplyBackdropConfigurationUpdate()
    {
        if (_lifecycle is not { } lifecycle)
        {
            return;
        }

        lifecycle.OnDefaultSystemBackdropConfigurationChanged();

        if (!lifecycle.IsConnected)
        {
            _lifecycle = null;
        }
    }
}
