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
    private readonly Action<DateTimeOffset> _refresh;
    private readonly SemaphoreSlim _stateGate = new(1, 1);
    private CancellationTokenSource? _visibleCancellation;
    private Task? _loopTask;
    private bool _isVisible;
    private bool _isDisposed;

    public TimerCoordinator(
        IClock clock,
        ITickSource tickSource,
        Action<DateTimeOffset> refresh)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(tickSource);
        ArgumentNullException.ThrowIfNull(refresh);

        _clock = clock;
        _tickSource = tickSource;
        _refresh = refresh;
    }

    public async Task SetVisibleAsync(bool isVisible)
    {
        Task? loopToStop = null;
        await _stateGate.WaitAsync().ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_isDisposed, this);
            if (_isVisible == isVisible)
            {
                return;
            }

            _isVisible = isVisible;
            if (isVisible)
            {
                _refresh(_clock.UtcNow.ToUniversalTime());
                _visibleCancellation = new CancellationTokenSource();
                _loopTask = RunVisibleLoopAsync(
                    _visibleCancellation.Token);
            }
            else
            {
                _visibleCancellation?.Cancel();
                loopToStop = _loopTask;
                _loopTask = null;
            }
        }
        finally
        {
            _stateGate.Release();
        }

        await AwaitCancellationAsync(loopToStop).ConfigureAwait(false);
        if (!isVisible)
        {
            _visibleCancellation?.Dispose();
            _visibleCancellation = null;
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

        await AwaitCancellationAsync(loopToStop).ConfigureAwait(false);
        cancellation?.Dispose();
    }

    private async Task RunVisibleLoopAsync(
        CancellationToken cancellationToken)
    {
        while (true)
        {
            await _tickSource.WaitForNextTickAsync(
                RefreshInterval,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            _refresh(_clock.UtcNow.ToUniversalTime());
        }
    }

    private static async Task AwaitCancellationAsync(Task? task)
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
        {
        }
    }
}
