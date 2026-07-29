namespace StaminaManager.Application;

internal sealed class TimerVisibilityController : IAsyncDisposable
{
    private readonly Func<bool, Task> _setVisibleAsync;
    private readonly Func<Task> _disposeTimerAsync;
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private AppPage _currentPage = AppPage.Overview;
    private bool _isWindowShown;
    private bool _isAppliedVisible;
    private bool _isDisposeRequested;
    private long _revision;
    private Task? _disposeTask;

    public TimerVisibilityController(TimerCoordinator timerCoordinator)
    {
        ArgumentNullException.ThrowIfNull(timerCoordinator);
        _setVisibleAsync = timerCoordinator.SetVisibleAsync;
        _disposeTimerAsync =
            () => timerCoordinator.DisposeAsync().AsTask();
    }

    internal TimerVisibilityController(
        Func<bool, Task> setVisibleAsync,
        Func<Task> disposeTimerAsync)
    {
        ArgumentNullException.ThrowIfNull(setVisibleAsync);
        ArgumentNullException.ThrowIfNull(disposeTimerAsync);
        _setVisibleAsync = setVisibleAsync;
        _disposeTimerAsync = disposeTimerAsync;
    }

    public Task SetWindowShownAsync(bool isShown) =>
        UpdateDesiredStateAsync(() => _isWindowShown = isShown);

    public Task SetCurrentPageAsync(AppPage currentPage)
    {
        if (!Enum.IsDefined(currentPage))
        {
            throw new ArgumentOutOfRangeException(nameof(currentPage));
        }

        return UpdateDesiredStateAsync(() => _currentPage = currentPage);
    }

    public ValueTask DisposeAsync()
    {
        lock (_stateLock)
        {
            if (_disposeTask is null)
            {
                _isDisposeRequested = true;
                _revision++;
                _disposeTask = DisposeCoreAsync();
            }

            return new ValueTask(_disposeTask);
        }
    }

    private Task UpdateDesiredStateAsync(Action updateState)
    {
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(
                _isDisposeRequested,
                this);
            updateState();
            _revision++;
        }

        return ReconcileAsync();
    }

    private async Task ReconcileAsync()
    {
        await _applyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            while (true)
            {
                bool desiredVisibility;
                long appliedRevision;
                lock (_stateLock)
                {
                    if (_isDisposeRequested)
                    {
                        return;
                    }

                    desiredVisibility = _isWindowShown
                        && _currentPage == AppPage.Overview;
                    appliedRevision = _revision;
                }

                if (_isAppliedVisible != desiredVisibility)
                {
                    await _setVisibleAsync(desiredVisibility)
                        .ConfigureAwait(false);
                    _isAppliedVisible = desiredVisibility;
                }

                lock (_stateLock)
                {
                    if (_isDisposeRequested
                        || appliedRevision == _revision)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            _applyGate.Release();
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _applyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _disposeTimerAsync().ConfigureAwait(false);
        }
        finally
        {
            _applyGate.Release();
        }
    }
}
