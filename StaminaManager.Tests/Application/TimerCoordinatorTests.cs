using StaminaManager.Application;
using StaminaManager.Tests.TestDoubles;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class TimerCoordinatorTests
{
    private static readonly DateTimeOffset InitialUtc = new(
        2026,
        7,
        25,
        12,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public async Task SetVisibleAsync_RefreshesImmediatelyAndEveryThirtySeconds()
    {
        FakeClock clock = new(InitialUtc);
        ManualTickSource ticks = new();
        RecordingUiDispatcher dispatcher = new();
        List<DateTimeOffset> refreshes = [];
        await using TimerCoordinator coordinator = new(
            clock,
            ticks,
            dispatcher,
            refreshes.Add);

        await coordinator.SetVisibleAsync(true);

        CollectionAssert.AreEqual(
            new[] { InitialUtc },
            refreshes);
        Assert.AreEqual(TimeSpan.FromSeconds(30), ticks.LastInterval);

        clock.UtcNow = InitialUtc.AddSeconds(30);
        await ticks.TickAsync();

        CollectionAssert.AreEqual(
            new[] { InitialUtc, InitialUtc.AddSeconds(30) },
            refreshes);
    }

    [TestMethod]
    public async Task SetVisibleAsync_HiddenStopsTicksAndResumeRefreshesNow()
    {
        FakeClock clock = new(InitialUtc);
        ManualTickSource ticks = new();
        RecordingUiDispatcher dispatcher = new();
        List<DateTimeOffset> refreshes = [];
        await using TimerCoordinator coordinator = new(
            clock,
            ticks,
            dispatcher,
            refreshes.Add);
        await coordinator.SetVisibleAsync(true);

        await coordinator.SetVisibleAsync(false);
        await ticks.TickAsync();
        Assert.HasCount(1, refreshes);

        clock.UtcNow = InitialUtc.AddMinutes(10);
        await coordinator.SetVisibleAsync(true);

        CollectionAssert.AreEqual(
            new[] { InitialUtc, InitialUtc.AddMinutes(10) },
            refreshes);
    }

    [TestMethod]
    public async Task DisposeAsync_CancelsPendingTickAndPreventsRestart()
    {
        FakeClock clock = new(InitialUtc);
        ManualTickSource ticks = new();
        RecordingUiDispatcher dispatcher = new();
        List<DateTimeOffset> refreshes = [];
        TimerCoordinator coordinator = new(
            clock,
            ticks,
            dispatcher,
            refreshes.Add);
        await coordinator.SetVisibleAsync(true);

        await coordinator.DisposeAsync();

        Assert.IsTrue(ticks.WasCancelled);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => coordinator.SetVisibleAsync(true));
    }

    [TestMethod]
    public async Task RefreshCallbacks_RunOnTheInjectedUiDispatcher()
    {
        FakeClock clock = new(InitialUtc);
        ManualTickSource ticks = new();
        RecordingUiDispatcher dispatcher = new();
        List<bool> dispatcherStates = [];
        await using TimerCoordinator coordinator = new(
            clock,
            ticks,
            dispatcher,
            _ => dispatcherStates.Add(dispatcher.IsExecuting));

        await coordinator.SetVisibleAsync(true);
        await ticks.TickAsync();

        CollectionAssert.AreEqual(
            new[] { true, true },
            dispatcherStates);
    }

    [TestMethod]
    public async Task SetVisibleAsync_ConcurrentRestartKeepsNewCancellation()
    {
        FakeClock clock = new(InitialUtc);
        DelayedCancellationTickSource ticks = new();
        RecordingUiDispatcher dispatcher = new();
        TimerCoordinator coordinator = new(
            clock,
            ticks,
            dispatcher,
            _ => { });
        await coordinator.SetVisibleAsync(true);

        Task hideTask = coordinator.SetVisibleAsync(false);
        await ticks.FirstCancellationObserved;
        await coordinator.SetVisibleAsync(true);
        ticks.ReleaseFirstCancellation();
        await hideTask;

        Task stopRestartedTask = coordinator.SetVisibleAsync(false);
        bool restartedWaitWasCancelled = ticks.SecondWaitWasCancelled;
        ticks.ReleaseSecondWait();
        try
        {
            await stopRestartedTask;
        }
        catch (InvalidOperationException)
        {
        }

        await coordinator.DisposeAsync();
        Assert.IsTrue(restartedWaitWasCancelled);
    }

    [TestMethod]
    public async Task SetVisibleAsync_ImmediateRefreshFailureCanRetry()
    {
        FakeClock clock = new(InitialUtc);
        ManualTickSource ticks = new();
        RecordingUiDispatcher dispatcher = new();
        bool shouldFail = true;
        int refreshCount = 0;
        bool failureRaisedOnDispatcher = false;
        await using TimerCoordinator coordinator = new(
            clock,
            ticks,
            dispatcher,
            _ =>
            {
                refreshCount++;
                if (shouldFail)
                {
                    throw new InvalidOperationException("refresh failure");
                }
            });
        coordinator.RefreshFailed += (_, _) =>
            failureRaisedOnDispatcher = dispatcher.IsExecuting;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => coordinator.SetVisibleAsync(true));
        Assert.IsTrue(failureRaisedOnDispatcher);
        shouldFail = false;

        await coordinator.SetVisibleAsync(true);

        Assert.AreEqual(2, refreshCount);
    }

    [TestMethod]
    public async Task TickFailure_IsExposedAndStopsVisibleState()
    {
        FakeClock clock = new(InitialUtc);
        ThrowingTickSource ticks = new();
        RecordingUiDispatcher dispatcher = new();
        int refreshCount = 0;
        await using TimerCoordinator coordinator = new(
            clock,
            ticks,
            dispatcher,
            _ => refreshCount++);

        await coordinator.SetVisibleAsync(true);

        Assert.IsInstanceOfType<IOException>(coordinator.LastError);
        await coordinator.SetVisibleAsync(true);
        Assert.AreEqual(2, refreshCount);
    }

    private sealed class ManualTickSource : ITickSource
    {
        private readonly object _syncRoot = new();
        private TaskCompletionSource? _pendingTick;
        private TaskCompletionSource _waitStarted = CreateTickSource();

        public TimeSpan? LastInterval { get; private set; }

        public bool WasCancelled { get; private set; }

        public async ValueTask WaitForNextTickAsync(
            TimeSpan interval,
            CancellationToken cancellationToken)
        {
            TaskCompletionSource pendingTick = CreateTickSource();
            lock (_syncRoot)
            {
                if (_pendingTick is not null)
                {
                    throw new InvalidOperationException(
                        "A tick wait is already pending.");
                }

                LastInterval = interval;
                _pendingTick = pendingTick;
                _waitStarted.TrySetResult();
            }

            try
            {
                await pendingTick.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                WasCancelled = true;
                throw;
            }
            finally
            {
                lock (_syncRoot)
                {
                    if (ReferenceEquals(_pendingTick, pendingTick))
                    {
                        _pendingTick = null;
                    }
                }
            }
        }

        public async Task TickAsync()
        {
            Task waitStarted;
            lock (_syncRoot)
            {
                waitStarted = _waitStarted.Task;
            }

            await waitStarted;

            TaskCompletionSource? completedTick;
            Task nextWaitStarted;
            lock (_syncRoot)
            {
                completedTick = _pendingTick;
                if (completedTick is null)
                {
                    return;
                }

                _waitStarted = CreateTickSource();
                nextWaitStarted = _waitStarted.Task;
            }

            completedTick.TrySetResult();
            await nextWaitStarted;
        }

        private static TaskCompletionSource CreateTickSource() => new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class DelayedCancellationTickSource : ITickSource
    {
        private readonly TaskCompletionSource _firstWait = CreateSource();
        private readonly TaskCompletionSource _firstCancellation =
            CreateSource();
        private readonly TaskCompletionSource _releaseFirst = CreateSource();
        private readonly TaskCompletionSource _releaseSecond = CreateSource();
        private int _waitCount;

        public Task FirstCancellationObserved => _firstCancellation.Task;

        public bool SecondWaitWasCancelled { get; private set; }

        public async ValueTask WaitForNextTickAsync(
            TimeSpan interval,
            CancellationToken cancellationToken)
        {
            int waitNumber = Interlocked.Increment(ref _waitCount);
            if (waitNumber == 1)
            {
                try
                {
                    await _firstWait.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    _firstCancellation.TrySetResult();
                    await _releaseFirst.Task;
                    throw;
                }
            }

            using CancellationTokenRegistration registration =
                cancellationToken.Register(
                    () => SecondWaitWasCancelled = true);
            await _releaseSecond.Task;
            cancellationToken.ThrowIfCancellationRequested();
            throw new InvalidOperationException("released second wait");
        }

        public void ReleaseFirstCancellation() =>
            _releaseFirst.TrySetResult();

        public void ReleaseSecondWait() => _releaseSecond.TrySetResult();

        private static TaskCompletionSource CreateSource() => new(
            TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ThrowingTickSource : ITickSource
    {
        public ValueTask WaitForNextTickAsync(
            TimeSpan interval,
            CancellationToken cancellationToken) =>
            ValueTask.FromException(
                new IOException("simulated tick failure"));
    }
}
