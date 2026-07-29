using StaminaManager.Application;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class TimerVisibilityControllerTests
{
    [TestMethod]
    public async Task OverviewShown_AppliesVisibleState()
    {
        RecordingTimerTarget target = new();
        await using TimerVisibilityController controller =
            target.CreateController();

        await controller.SetWindowShownAsync(true);

        CollectionAssert.AreEqual(
            new[] { true },
            target.VisibilityChanges);
    }

    [TestMethod]
    public async Task SettingsStopsAndOverviewReturnRestartsImmediately()
    {
        RecordingTimerTarget target = new();
        await using TimerVisibilityController controller =
            target.CreateController();
        await controller.SetWindowShownAsync(true);

        await controller.SetCurrentPageAsync(AppPage.Settings);
        await controller.SetCurrentPageAsync(AppPage.Settings);
        await controller.SetCurrentPageAsync(AppPage.Overview);

        CollectionAssert.AreEqual(
            new[] { true, false, true },
            target.VisibilityChanges);
    }

    [TestMethod]
    public async Task TrayShowRestartsOnlyWhenOverviewIsCurrent()
    {
        RecordingTimerTarget target = new();
        await using TimerVisibilityController controller =
            target.CreateController();
        await controller.SetWindowShownAsync(true);

        await controller.SetWindowShownAsync(false);
        await controller.SetWindowShownAsync(true);
        await controller.SetCurrentPageAsync(AppPage.Settings);
        await controller.SetWindowShownAsync(false);
        await controller.SetWindowShownAsync(true);

        CollectionAssert.AreEqual(
            new[] { true, false, true, false },
            target.VisibilityChanges);
    }

    [TestMethod]
    public async Task RapidEvents_FinalDesiredStateWins()
    {
        TaskCompletionSource firstApplyStarted = CreateSource();
        TaskCompletionSource releaseFirstApply = CreateSource();
        List<bool> visibilityChanges = [];
        int applyCount = 0;
        await using TimerVisibilityController controller = new(
            async isVisible =>
            {
                visibilityChanges.Add(isVisible);
                if (Interlocked.Increment(ref applyCount) == 1)
                {
                    firstApplyStarted.TrySetResult();
                    await releaseFirstApply.Task;
                }
            },
            () => Task.CompletedTask);

        Task showTask = controller.SetWindowShownAsync(true);
        await firstApplyStarted.Task;
        Task settingsTask = controller.SetCurrentPageAsync(
            AppPage.Settings);
        Task overviewTask = controller.SetCurrentPageAsync(
            AppPage.Overview);
        Task hideTask = controller.SetWindowShownAsync(false);
        releaseFirstApply.TrySetResult();

        await Task.WhenAll(
            showTask,
            settingsTask,
            overviewTask,
            hideTask);

        Assert.IsFalse(visibilityChanges[^1]);
        Assert.HasCount(2, visibilityChanges);
    }

    [TestMethod]
    public async Task DisposeAsync_DisposesOnceAndRejectsRestart()
    {
        RecordingTimerTarget target = new();
        TimerVisibilityController controller = target.CreateController();
        await controller.SetWindowShownAsync(true);

        ValueTask firstDispose = controller.DisposeAsync();
        ValueTask secondDispose = controller.DisposeAsync();
        await Task.WhenAll(
            firstDispose.AsTask(),
            secondDispose.AsTask());

        Assert.AreEqual(1, target.DisposeCount);
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => controller.SetWindowShownAsync(true));
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(
            () => controller.SetCurrentPageAsync(AppPage.Overview));
    }

    private static TaskCompletionSource CreateSource() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RecordingTimerTarget
    {
        public List<bool> VisibilityChanges { get; } = [];

        public int DisposeCount { get; private set; }

        public TimerVisibilityController CreateController() => new(
            isVisible =>
            {
                VisibilityChanges.Add(isVisible);
                return Task.CompletedTask;
            },
            () =>
            {
                DisposeCount++;
                return Task.CompletedTask;
            });
    }
}
