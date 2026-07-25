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
        List<DateTimeOffset> refreshes = [];
        await using TimerCoordinator coordinator = new(
            clock,
            ticks,
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
        List<DateTimeOffset> refreshes = [];
        await using TimerCoordinator coordinator = new(
            clock,
            ticks,
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
        List<DateTimeOffset> refreshes = [];
        TimerCoordinator coordinator = new(
            clock,
            ticks,
            refreshes.Add);
        await coordinator.SetVisibleAsync(true);

        await coordinator.DisposeAsync();

        Assert.IsTrue(ticks.WasCancelled);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => coordinator.SetVisibleAsync(true));
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
}
