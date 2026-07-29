using StaminaManager.Application;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class ShutdownSequenceTests
{
    [TestMethod]
    public async Task ReentrantRequests_ShareCleanupAndCompleteExitOnce()
    {
        TaskCompletionSource cleanupStarted = CreateSource();
        TaskCompletionSource releaseCleanup = CreateSource();
        int cleanupCount = 0;
        int exitCount = 0;
        ShutdownSequence sequence = new(
            async () =>
            {
                cleanupCount++;
                cleanupStarted.TrySetResult();
                await releaseCleanup.Task;
            },
            _ => { },
            () => exitCount++);

        Task normalClose = sequence.RequestAsync();
        await cleanupStarted.Task;
        Task trayExit = sequence.RequestAsync();

        Assert.AreSame(normalClose, trayExit);
        Assert.IsTrue(sequence.IsRequested);
        Assert.IsFalse(sequence.IsCompleted);
        Assert.AreEqual(0, exitCount);

        releaseCleanup.TrySetResult();
        await Task.WhenAll(normalClose, trayExit);
        await sequence.RequestAsync();

        Assert.AreEqual(1, cleanupCount);
        Assert.AreEqual(1, exitCount);
        Assert.IsTrue(sequence.IsCompleted);
    }

    [TestMethod]
    public async Task CleanupFailure_IsReportedBeforeExitStillCompletes()
    {
        IOException failure = new("cleanup failure");
        Exception? reported = null;
        int exitCount = 0;
        ShutdownSequence sequence = new(
            () => Task.FromException(failure),
            exception => reported = exception,
            () => exitCount++);

        await sequence.RequestAsync();

        Assert.AreSame(failure, reported);
        Assert.AreSame(failure, sequence.LastError);
        Assert.AreEqual(1, exitCount);
        Assert.IsTrue(sequence.IsCompleted);
    }

    private static TaskCompletionSource CreateSource() => new(
        TaskCreationOptions.RunContinuationsAsynchronously);
}
