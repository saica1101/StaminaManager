using StaminaManager.Infrastructure.Windows;

namespace StaminaManager.Tests.Infrastructure.Windows;

[TestClass]
public sealed class UiWorkQueueTests
{
    [TestMethod]
    public void Constructor_NullDispatcherQueue_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new DispatcherQueueUiWorkQueue(null!));
    }

    [TestMethod]
    public void CoalescingAction_NullQueue_Throws()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => new CoalescingUiAction(null!, () => { }));
    }

    [TestMethod]
    public void CoalescingAction_NullAction_Throws()
    {
        TestUiWorkQueue queue = new();

        Assert.ThrowsExactly<ArgumentNullException>(
            () => new CoalescingUiAction(queue, null!));
    }

    [TestMethod]
    public void CoalescingAction_ThreeRequestsBeforeDispatch_QueuesOnce()
    {
        TestUiWorkQueue queue = new();
        int actionCount = 0;
        CoalescingUiAction action = new(queue, () => actionCount++);

        Assert.IsTrue(action.Request());
        Assert.IsTrue(action.Request());
        Assert.IsTrue(action.Request());

        Assert.AreEqual(1, queue.EnqueueCount);
        Assert.AreEqual(0, actionCount);

        queue.RunNext();

        Assert.AreEqual(1, actionCount);
    }

    [TestMethod]
    public void CoalescingAction_RequestAfterExecution_QueuesAgain()
    {
        TestUiWorkQueue queue = new();
        int actionCount = 0;
        CoalescingUiAction action = new(queue, () => actionCount++);

        Assert.IsTrue(action.Request());
        queue.RunNext();

        Assert.IsTrue(action.Request());
        queue.RunNext();

        Assert.AreEqual(2, queue.EnqueueCount);
        Assert.AreEqual(2, actionCount);
    }

    [TestMethod]
    public void CoalescingAction_RequestDuringExecution_QueuesAgain()
    {
        TestUiWorkQueue queue = new();
        int actionCount = 0;
        CoalescingUiAction? action = null;
        action = new CoalescingUiAction(queue, () =>
        {
            actionCount++;
            if (actionCount == 1)
            {
                Assert.IsTrue(action!.Request());
            }
        });

        Assert.IsTrue(action.Request());
        queue.RunNext();
        queue.RunNext();

        Assert.AreEqual(2, queue.EnqueueCount);
        Assert.AreEqual(2, actionCount);
    }

    [TestMethod]
    public void CoalescingAction_EnqueueFailure_AllowsRetry()
    {
        TestUiWorkQueue queue = new(isEnqueueAccepted: false);
        int actionCount = 0;
        CoalescingUiAction action = new(queue, () => actionCount++);

        Assert.IsFalse(action.Request());
        queue.IsEnqueueAccepted = true;

        Assert.IsTrue(action.Request());
        queue.RunNext();

        Assert.AreEqual(2, queue.EnqueueCount);
        Assert.AreEqual(1, actionCount);
    }

    [TestMethod]
    public void CoalescingAction_QueuedAction_ReadsLatestStateAtExecution()
    {
        TestUiWorkQueue queue = new();
        string currentTheme = "Light";
        string? appliedTheme = null;
        CoalescingUiAction action = new(
            queue,
            () => appliedTheme = currentTheme);

        Assert.IsTrue(action.Request());
        currentTheme = "Dark";
        Assert.IsTrue(action.Request());

        queue.RunNext();

        Assert.AreEqual("Dark", appliedTheme);
        Assert.AreEqual(1, queue.EnqueueCount);
    }

    private sealed class TestUiWorkQueue(
        bool isEnqueueAccepted = true) : IUiWorkQueue
    {
        private readonly Queue<Action> _actions = new();

        public bool IsEnqueueAccepted { get; set; } = isEnqueueAccepted;

        public int EnqueueCount { get; private set; }

        public bool TryEnqueue(Action action)
        {
            EnqueueCount++;
            if (!IsEnqueueAccepted)
            {
                return false;
            }

            _actions.Enqueue(action);
            return true;
        }

        public void RunNext()
        {
            Assert.IsTrue(_actions.TryDequeue(out Action? action));
            action();
        }
    }
}
