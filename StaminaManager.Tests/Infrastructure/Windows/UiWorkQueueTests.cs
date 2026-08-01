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
}
