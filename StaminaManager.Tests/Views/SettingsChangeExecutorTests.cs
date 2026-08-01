using StaminaManager.Infrastructure.Windows;
using StaminaManager.Core.Models;
using StaminaManager.Views;

namespace StaminaManager.Tests.Views;

[TestClass]
public sealed class SettingsChangeExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_UnexpectedExceptionIsReportedAndSynchronized()
    {
        int failureCount = 0;
        int synchronizationCount = 0;

        await SettingsChangeExecutor.ExecuteAsync(
            () => throw new InvalidOperationException(
                "service implementation detail"),
            () => failureCount++,
            () => synchronizationCount++);

        Assert.AreEqual(1, failureCount);
        Assert.AreEqual(1, synchronizationCount);
    }

    [TestMethod]
    public async Task DeferredExecuteAsync_QueuedActionまで変更を遅延する()
    {
        RecordingUiWorkQueue queue = new();
        List<string> calls = [];
        DeferredSettingsChangeExecutor executor = new(queue);

        Task pending = executor.ExecuteAsync(
            () =>
            {
                calls.Add("change");
                return Task.CompletedTask;
            },
            () => calls.Add("failure"),
            () => calls.Add("sync"));

        Assert.IsEmpty(calls);
        Assert.IsFalse(pending.IsCompleted);

        queue.RunNext();
        await pending;
        calls.Add("completed");

        CollectionAssert.AreEqual(
            new[] { "change", "sync", "completed" },
            calls);
    }

    [TestMethod]
    public async Task DeferredExecuteAsync_投入失敗は通知と同期を各1回行う()
    {
        RecordingUiWorkQueue queue = new(isEnqueueAccepted: false);
        int changeCount = 0;
        int failureCount = 0;
        int synchronizationCount = 0;
        DeferredSettingsChangeExecutor executor = new(queue);

        await executor.ExecuteAsync(
            () =>
            {
                changeCount++;
                return Task.CompletedTask;
            },
            () => failureCount++,
            () => synchronizationCount++);

        Assert.AreEqual(0, changeCount);
        Assert.AreEqual(1, failureCount);
        Assert.AreEqual(1, synchronizationCount);
    }

    [TestMethod]
    public async Task DeferredExecuteAsync_完了単位で投入順に直列化する()
    {
        RecordingUiWorkQueue queue = new();
        TaskCompletionSource firstStarted = CreateSource();
        TaskCompletionSource releaseFirst = CreateSource();
        List<string> changes = [];
        DeferredSettingsChangeExecutor executor = new(queue);

        Task first = executor.ExecuteAsync(
            async () =>
            {
                changes.Add("first");
                firstStarted.TrySetResult();
                await releaseFirst.Task;
            },
            () => { },
            () => { });
        Task second = executor.ExecuteAsync(
            () =>
            {
                changes.Add("second");
                return Task.CompletedTask;
            },
            () => { },
            () => { });

        queue.RunNext();
        await firstStarted.Task;
        queue.RunNext();

        CollectionAssert.AreEqual(new[] { "first" }, changes);

        releaseFirst.TrySetResult();
        await Task.WhenAll(first, second);

        CollectionAssert.AreEqual(
            new[] { "first", "second" },
            changes);
    }

    [TestMethod]
    public async Task DeferredExecuteAsync_変更例外は通知して正常完了する()
    {
        RecordingUiWorkQueue queue = new();
        int failureCount = 0;
        int synchronizationCount = 0;
        DeferredSettingsChangeExecutor executor = new(queue);

        Task pending = executor.ExecuteAsync(
            () => throw new InvalidOperationException("sensitive detail"),
            () => failureCount++,
            () => synchronizationCount++);

        queue.RunNext();
        await pending;

        Assert.AreEqual(1, failureCount);
        Assert.AreEqual(1, synchronizationCount);
    }

    [TestMethod]
    public async Task DeferredExecuteAsync_キュー投入前の要求値をそれぞれ保持する()
    {
        RecordingUiWorkQueue queue = new();
        List<AppTheme> applied = [];
        DeferredSettingsChangeExecutor executor = new(queue);

        Task first = executor.ExecuteAsync(
            AppTheme.Dark,
            theme =>
            {
                applied.Add(theme);
                return Task.CompletedTask;
            },
            () => { },
            () => { });
        Task second = executor.ExecuteAsync(
            AppTheme.Light,
            theme =>
            {
                applied.Add(theme);
                return Task.CompletedTask;
            },
            () => { },
            () => { });

        queue.RunNext();
        queue.RunNext();
        await Task.WhenAll(first, second);

        CollectionAssert.AreEqual(
            new[] { AppTheme.Dark, AppTheme.Light },
            applied);
    }

    private static TaskCompletionSource CreateSource() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class RecordingUiWorkQueue(
        bool isEnqueueAccepted = true) : IUiWorkQueue
    {
        private readonly Queue<Action> _actions = [];

        public bool TryEnqueue(Action action)
        {
            if (!isEnqueueAccepted)
            {
                return false;
            }

            _actions.Enqueue(action);
            return true;
        }

        public void RunNext() => _actions.Dequeue()();
    }
}
