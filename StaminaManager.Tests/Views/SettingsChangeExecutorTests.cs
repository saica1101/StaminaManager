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
}
