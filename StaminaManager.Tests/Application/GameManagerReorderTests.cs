using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Tests.TestDoubles;
using System.Collections.Immutable;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class GameManagerReorderTests
{
    private static readonly DateTimeOffset NowUtc = new(
        2026,
        8,
        14,
        0,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public async Task ReorderAsync_MovesFirstGameToTargetPositionAndPersistsOrder()
    {
        RecordingDataStore store = new();
        GameEntry first = CreateEntry("A", 0);
        GameEntry second = CreateEntry("B", 1);
        GameEntry third = CreateEntry("C", 2);
        GameEntry fourth = CreateEntry("D", 3);
        GameManager manager = await CreateManagerAsync(
            store,
            first,
            second,
            third,
            fourth);

        bool reordered = await manager.ReorderAsync(
            first.Id,
            fourth.Id,
            CancellationToken.None);

        Assert.IsTrue(reordered);
        CollectionAssert.AreEqual(
            new[] { "B", "C", "D", "A" },
            manager.Games.Select(game => game.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2, 3 },
            manager.Games.Select(game => game.SortOrder).ToArray());
        CollectionAssert.AreEqual(
            new[] { "B", "C", "D", "A" },
            store.LastSaved!.Games.Select(game => game.Name).ToArray());
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public async Task ReorderAsync_MovesLastGameToFirstPosition()
    {
        RecordingDataStore store = new();
        GameEntry first = CreateEntry("A", 0);
        GameEntry second = CreateEntry("B", 1);
        GameEntry third = CreateEntry("C", 2);
        GameEntry fourth = CreateEntry("D", 3);
        GameManager manager = await CreateManagerAsync(
            store,
            first,
            second,
            third,
            fourth);

        bool reordered = await manager.ReorderAsync(
            fourth.Id,
            first.Id,
            CancellationToken.None);

        Assert.IsTrue(reordered);
        CollectionAssert.AreEqual(
            new[] { "D", "A", "B", "C" },
            manager.Games.Select(game => game.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2, 3 },
            manager.Games.Select(game => game.SortOrder).ToArray());
    }

    [TestMethod]
    public async Task ReorderAsync_SameGameDoesNotPersist()
    {
        RecordingDataStore store = new();
        GameEntry only = CreateEntry("Only", 0);
        GameManager manager = await CreateManagerAsync(store, only);

        bool reordered = await manager.ReorderAsync(
            only.Id,
            only.Id,
            CancellationToken.None);

        Assert.IsFalse(reordered);
        Assert.AreEqual(0, store.SaveCount);
        Assert.AreEqual(only, manager.Games.Single());
    }

    [TestMethod]
    public async Task ReorderAsync_MissingGameDoesNotPersist()
    {
        RecordingDataStore store = new();
        GameEntry first = CreateEntry("A", 0);
        GameEntry second = CreateEntry("B", 1);
        GameManager manager = await CreateManagerAsync(store, first, second);

        bool reordered = await manager.ReorderAsync(
            first.Id,
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.IsFalse(reordered);
        Assert.AreEqual(0, store.SaveCount);
        CollectionAssert.AreEqual(
            new[] { "A", "B" },
            manager.Games.Select(game => game.Name).ToArray());
    }

    [TestMethod]
    public async Task ReorderAsync_SaveFailureKeepsPublishedOrder()
    {
        RecordingDataStore store = new()
        {
            SaveException = new IOException("simulated persistence failure"),
        };
        GameEntry first = CreateEntry("A", 0);
        GameEntry second = CreateEntry("B", 1);
        GameEntry third = CreateEntry("C", 2);
        GameManager manager = await CreateManagerAsync(
            store,
            first,
            second,
            third);

        await Assert.ThrowsExactlyAsync<IOException>(
            () => manager.ReorderAsync(
                first.Id,
                third.Id,
                CancellationToken.None));

        CollectionAssert.AreEqual(
            new[] { "A", "B", "C" },
            manager.Games.Select(game => game.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2 },
            manager.Games.Select(game => game.SortOrder).ToArray());
        CollectionAssert.AreEqual(
            new[] { "B", "C", "A" },
            store.LastAttempted!.Games.Select(game => game.Name).ToArray());
        Assert.AreEqual(1, store.SaveCount);
    }

    private static async Task<GameManager> CreateManagerAsync(
        RecordingDataStore store,
        params GameEntry[] games)
    {
        GameManager manager = new(
            store,
            new FakeClock(NowUtc),
            AppSettings.CreateDefault(AppTheme.Light));

        await manager.InitializeAsync(
            new DataEnvelope(
                DataEnvelope.CurrentSchemaVersion,
                games.ToImmutableArray(),
                AppSettings.CreateDefault(AppTheme.Light)),
            CancellationToken.None);

        return manager;
    }

    private static GameEntry CreateEntry(string name, int sortOrder) => new(
        Guid.NewGuid(),
        name,
        BaseStamina: 40,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        RecordedAtUtc: NowUtc.AddHours(-1),
        ImageAssetId: null,
        sortOrder,
        RecoverySeconds: 0,
        IsNotificationEnabled: true);

    private sealed class RecordingDataStore : ILocalDataStore
    {
        public DataEnvelope? LastSaved { get; private set; }

        public DataEnvelope? LastAttempted { get; private set; }

        public int SaveCount { get; private set; }

        public Exception? SaveException { get; init; }

        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromException<DataLoadResult>(
                new NotSupportedException());

        public Task SaveAsync(
            DataEnvelope envelope,
            CancellationToken cancellationToken)
        {
            LastAttempted = envelope;
            SaveCount++;

            if (SaveException is not null)
            {
                return Task.FromException(SaveException);
            }

            LastSaved = envelope;
            return Task.CompletedTask;
        }

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken) =>
            Task.FromException<RecoveryPromotionResult>(
                new NotSupportedException());
    }
}
