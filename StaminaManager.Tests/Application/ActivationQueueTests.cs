using StaminaManager.Application;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class ActivationQueueTests
{
    [TestMethod]
    public void Attach_登録前のactivationを受信順に配送する()
    {
        ActivationQueue<string> queue = new();
        queue.Enqueue("first");
        queue.Enqueue("second");
        List<string> received = [];

        queue.Attach(received.Add);

        CollectionAssert.AreEqual(
            new[] { "first", "second" },
            received);
    }

    [TestMethod]
    public void Enqueue_Attach後は直接配送する()
    {
        ActivationQueue<string> queue = new();
        List<string> received = [];
        queue.Attach(received.Add);

        queue.Enqueue("redirected");

        CollectionAssert.AreEqual(
            new[] { "redirected" },
            received);
    }

    [TestMethod]
    public void Attach_2回目のhandler登録を拒否する()
    {
        ActivationQueue<string> queue = new();
        queue.Attach(_ => { });

        Assert.Throws<InvalidOperationException>(
            () => queue.Attach(_ => { }));
    }
}
