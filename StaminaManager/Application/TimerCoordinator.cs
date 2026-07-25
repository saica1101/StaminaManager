using StaminaManager.Core.Abstractions;

namespace StaminaManager.Application;

public interface ITickSource
{
    ValueTask WaitForNextTickAsync(
        TimeSpan interval,
        CancellationToken cancellationToken);
}

public sealed class SystemTickSource : ITickSource
{
    public async ValueTask WaitForNextTickAsync(
        TimeSpan interval,
        CancellationToken cancellationToken)
    {
        await Task.Delay(interval, cancellationToken);
    }
}

public sealed class TimerCoordinator : IAsyncDisposable
{
    public static readonly TimeSpan RefreshInterval =
        TimeSpan.FromSeconds(30);

    private readonly IClock _clock;
    private readonly ITickSource _tickSource;
    private readonly IUiDispatcher _uiDispatcher;
    private readonly Action<DateTimeOffset> _refresh;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private CancellationTokenSource? _visibleCancellation;
    private Task? _loopTask;
    private bool _isVisible;
    private bool _isDisposed;

    public TimerCoordinator(
        IClock clock,
        ITickSource tickSource,
        IUiDispatcher uiDispatcher,
        Action<DateTimeOffset> refresh)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(tickSource);
        ArgumentNullException.ThrowIfNull(uiDispatcher);
        ArgumentNullException.ThrowIfNull(refresh);

        _clock = clock;
        _tickSource = tickSource;
        _uiDispatcher = uiDispatcher;
        _refresh = refresh;
    }

    public Exception? LastError { get; private set; }

    public async Task SetVisibleAsync(bool isVisible)
    {
        if (!isVisible)
        {
            await StopAsync().ConfigureAwait(false);
            return;
        }

        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isVisible && _loopTask is { IsCompleted: false })
            {
                return;
            }

            _visibleCancellation?.Dispose();
            _visibleCancellation = null;
            _loopTask = null;
            _isVisible = false;
            try
            {
                await DispatchRefreshAsync(CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                LastError = exception;
                throw;
            }

            LastError = null;
            CancellationTokenSource cancellation = new();
            _visibleCancellation = cancellation;
            _loopTask = RunVisibleLoopAsync(cancellation.Token);
            _isVisible = true;
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task? loopToStop;
        CancellationTokenSource? cancellation;
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _isVisible = false;
            cancellation = _visibleCancellation;
            cancellation?.Cancel();
            loopToStop = _loopTask;
            _loopTask = null;
            _visibleCancellation = null;
        }
        finally
        {
            _stateGate.Release();
        }

        await AwaitStoppedLoopAsync(loopToStop, cancellation)
            .ConfigureAwait(false);
        cancellation?.Dispose();
    }

    private async Task StopAsync()
    {
        Task? loopToStop;
        CancellationTokenSource? cancellation;
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (!_isVisible && _loopTask is null)
            {
                return;
            }

            _isVisible = false;
            cancellation = _visibleCancellation;
            loopToStop = _loopTask;
            _visibleCancellation = null;
            _loopTask = null;
            cancellation?.Cancel();
        }
        finally
        {
            _stateGate.Release();
        }

        await AwaitStoppedLoopAsync(loopToStop, cancellation)
            .ConfigureAwait(false);
        cancellation?.Dispose();
    }

    private async Task RunVisibleLoopAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await _tickSource.WaitForNextTickAsync(
                    RefreshInterval,
                    cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await DispatchRefreshAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            LastError = exception;
        }
    }

    private Task DispatchRefreshAsync(CancellationToken cancellationToken) =>
        _uiDispatcher.InvokeAsync(
            () => _refresh(_clock.UtcNow.ToUniversalTime()),
            cancellationToken);

    private static async Task AwaitStoppedLoopAsync(
        Task? task,
        CancellationTokenSource? cancellation)
    {
        if (task is null)
        {
            return;
        }

        try
        {
            await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellation?.IsCancellationRequested == true)
        {
        }
    }
}
