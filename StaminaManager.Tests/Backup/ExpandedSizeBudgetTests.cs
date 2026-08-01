using StaminaManager.Infrastructure.Backup;

namespace StaminaManager.Tests.Backup;

[TestClass]
public sealed class ExpandedSizeBudgetTests
{
    [TestMethod]
    public void Consume_上限内の実測値を累積する()
    {
        ExpandedSizeBudget budget = new(maxBytes: 10);

        budget.Consume(4);
        budget.Consume(6);

        Assert.AreEqual(10L, budget.ConsumedBytes);
    }

    [TestMethod]
    public void Consume_上限を超えるチャンクを拒否する()
    {
        ExpandedSizeBudget budget = new(maxBytes: 10);
        budget.Consume(7);

        Assert.ThrowsExactly<InvalidDataException>(
            () => budget.Consume(4));
        Assert.AreEqual(7L, budget.ConsumedBytes);
    }

    [TestMethod]
    public void Consume_負数を拒否する()
    {
        ExpandedSizeBudget budget = new(maxBytes: 10);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => budget.Consume(-1));
    }
}
