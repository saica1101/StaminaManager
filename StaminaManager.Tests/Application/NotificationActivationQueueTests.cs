using StaminaManager.Application;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class NotificationActivationQueueTests
{
    [TestMethod]
    public async Task DrainAsync_HandlerFailureIsObservedAndReported()
    {
        Guid gameId = Guid.NewGuid();
        NotificationActivationQueue queue = new();
        queue.Enqueue(gameId);
        List<(Guid GameId, Exception Error)> failures = [];

        await queue.DrainAsync(
            _ => throw new IOException("focus failed"),
            (failedGameId, exception) =>
            {
                failures.Add((failedGameId, exception));
                return Task.CompletedTask;
            });

        Assert.HasCount(1, failures);
        Assert.AreEqual(gameId, failures[0].GameId);
        Assert.IsInstanceOfType<IOException>(failures[0].Error);
    }

    [TestMethod]
    public async Task DrainAsync_DeduplicatesUntilActivationCompletes()
    {
        Guid gameId = Guid.NewGuid();
        NotificationActivationQueue queue = new();
        Assert.IsTrue(queue.Enqueue(gameId));
        TaskCompletionSource started = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        int handledCount = 0;

        Task drain = queue.DrainAsync(
            async _ =>
            {
                started.SetResult();
                await release.Task;
                handledCount++;
            },
            (_, _) => Task.CompletedTask);
        await started.Task;

        Assert.IsFalse(queue.Enqueue(gameId));
        release.SetResult();
        await drain;

        Assert.AreEqual(1, handledCount);
        Assert.IsTrue(queue.Enqueue(gameId));
    }
}
