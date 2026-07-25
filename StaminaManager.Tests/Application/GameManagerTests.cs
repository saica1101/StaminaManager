using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using StaminaManager.Tests.TestDoubles;
using System.Collections.Immutable;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class GameManagerTests
{
    private static readonly DateTimeOffset NowUtc = new(
        2026,
        7,
        25,
        12,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public async Task AddAsync_AppendsGamesInRegistrationOrder()
    {
        RecordingDataStore store = new();
        GameManager manager = CreateManager(store);

        GameEntry first = await manager.AddAsync(
            CreateDraft("First"),
            CancellationToken.None);
        GameEntry second = await manager.AddAsync(
            CreateDraft("Second"),
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { "First", "Second" },
            manager.Games.Select(game => game.Name).ToArray());
        Assert.AreEqual(0, first.SortOrder);
        Assert.AreEqual(1, second.SortOrder);
        Assert.AreEqual(NowUtc, first.RecordedAtUtc);
        Assert.HasCount(2, store.LastSaved!.Games);
    }

    [TestMethod]
    public async Task EditAsync_DelegatesMetadataRulesToGameEditPolicy()
    {
        RecordingDataStore store = new();
        GameEntry original = CreateEntry(
            Guid.Parse("bd9828bd-2c9b-4328-a2b5-c3697055d718"),
            "Original",
            sortOrder: 0) with
        {
            BaseStamina = 20,
            RecordedAtUtc = NowUtc.AddHours(-2),
        };
        GameManager manager = CreateManager(store, original);
        GameDraft initialDraft = new(
            original.Name,
            CurrentStamina: 44,
            original.MaxStamina,
            original.RecoveryMinutes,
            original.ImageAssetId);
        GameDraft editedDraft = initialDraft with { Name = "Renamed" };

        GameEntry edited = await manager.EditAsync(
            original.Id,
            initialDraft,
            editedDraft,
            CancellationToken.None);

        Assert.AreEqual("Renamed", edited.Name);
        Assert.AreEqual(original.BaseStamina, edited.BaseStamina);
        Assert.AreEqual(original.RecordedAtUtc, edited.RecordedAtUtc);
        Assert.AreEqual(original.SortOrder, edited.SortOrder);
        Assert.AreEqual(edited, store.LastSaved!.Games[0]);
    }

    [TestMethod]
    public async Task EditAsync_ResetsStaminaBaselineThroughEditPolicy()
    {
        RecordingDataStore store = new();
        GameEntry original = CreateEntry(Guid.NewGuid(), "Game", 0);
        GameManager manager = CreateManager(store, original);
        GameDraft initialDraft = CreateDraft("Game") with
        {
            CurrentStamina = 40,
        };
        GameDraft editedDraft = initialDraft with
        {
            CurrentStamina = 55,
        };

        GameEntry edited = await manager.EditAsync(
            original.Id,
            initialDraft,
            editedDraft,
            CancellationToken.None);

        Assert.AreEqual(55, edited.BaseStamina);
        Assert.AreEqual(NowUtc, edited.RecordedAtUtc);
    }

    [TestMethod]
    public async Task DeleteAsync_NormalizesRemainingSortOrders()
    {
        RecordingDataStore store = new();
        GameEntry first = CreateEntry(Guid.NewGuid(), "First", 0);
        GameEntry second = CreateEntry(Guid.NewGuid(), "Second", 1);
        GameEntry third = CreateEntry(Guid.NewGuid(), "Third", 2);
        GameManager manager = CreateManager(store, first, second, third);

        bool deleted = await manager.DeleteAsync(
            second.Id,
            CancellationToken.None);

        Assert.IsTrue(deleted);
        CollectionAssert.AreEqual(
            new[] { "First", "Third" },
            manager.Games.Select(game => game.Name).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1 },
            manager.Games.Select(game => game.SortOrder).ToArray());
        CollectionAssert.AreEqual(
            new[] { 0, 1 },
            store.LastSaved!.Games
                .Select(game => game.SortOrder)
                .ToArray());
    }

    [TestMethod]
    public async Task DeleteAsync_MissingGameDoesNotPersist()
    {
        RecordingDataStore store = new();
        GameManager manager = CreateManager(
            store,
            CreateEntry(Guid.NewGuid(), "Only", 0));

        bool deleted = await manager.DeleteAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.IsFalse(deleted);
        Assert.AreEqual(0, store.SaveCount);
    }

    [TestMethod]
    public async Task AddAsync_RejectsTheHundredAndFirstGame()
    {
        RecordingDataStore store = new();
        GameEntry[] games = Enumerable.Range(0, 100)
            .Select(index => CreateEntry(
                Guid.NewGuid(),
                $"Game {index}",
                index))
            .ToArray();
        GameManager manager = CreateManager(store, games);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.AddAsync(
                CreateDraft("Too many"),
                CancellationToken.None));

        Assert.HasCount(100, manager.Games);
        Assert.AreEqual(0, store.SaveCount);
    }

    [TestMethod]
    public async Task EditAsync_SaveFailureKeepsPublishedStateAndCallerDraft()
    {
        RecordingDataStore store = new()
        {
            SaveException = new IOException("simulated persistence failure"),
        };
        GameEntry original = CreateEntry(Guid.NewGuid(), "Original", 0);
        GameManager manager = CreateManager(store, original);
        GameDraft initialDraft = CreateDraft("Original");
        GameDraft editedDraft = initialDraft with
        {
            Name = "Unsaved draft",
            CurrentStamina = 75,
        };

        await Assert.ThrowsExactlyAsync<IOException>(() => manager.EditAsync(
            original.Id,
            initialDraft,
            editedDraft,
            CancellationToken.None));

        Assert.AreEqual(original, manager.Games.Single());
        Assert.AreEqual("Unsaved draft", editedDraft.Name);
        Assert.AreEqual(75, editedDraft.CurrentStamina);
    }

    private static GameManager CreateManager(
        RecordingDataStore store,
        params GameEntry[] games)
    {
        FakeClock clock = new(NowUtc);
        GameManager manager = new(
            store,
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        manager.Initialize(new DataEnvelope(
            DataEnvelope.CurrentSchemaVersion,
            games.ToImmutableArray(),
            AppSettings.CreateDefault(AppTheme.Light)));
        return manager;
    }

    private static GameDraft CreateDraft(string name) => new(
        name,
        CurrentStamina: 40,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        ImageAssetId: null);

    private static GameEntry CreateEntry(
        Guid id,
        string name,
        int sortOrder) => new(
            id,
            name,
            BaseStamina: 40,
            MaxStamina: 100,
            RecoveryMinutes: 5,
            RecordedAtUtc: NowUtc.AddHours(-1),
            ImageAssetId: null,
            sortOrder);

    private sealed class RecordingDataStore : ILocalDataStore
    {
        public DataEnvelope? LastSaved { get; private set; }

        public int SaveCount { get; private set; }

        public Exception? SaveException { get; init; }

        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            DataEnvelope envelope,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
            throw new NotSupportedException();
    }
}
