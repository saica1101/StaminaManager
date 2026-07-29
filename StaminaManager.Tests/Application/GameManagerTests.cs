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
        GameManager manager = await CreateManagerAsync(store);

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
    public async Task AddAsync_FirstGameSavesSelectionWithGameOnce()
    {
        RecordingDataStore store = new();
        GameManager manager = await CreateManagerAsync(store);

        GameEntry added = await manager.AddAsync(
            CreateDraft("First"),
            CancellationToken.None);

        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual(added.Id, store.LastSaved!.Games.Single().Id);
        Assert.AreEqual(
            added.Id,
            store.LastSaved.Settings.SelectedCompactGameId);
        Assert.AreEqual(store.LastSaved, manager.CurrentData);
    }

    [TestMethod]
    public async Task AddAsync_FirstGameSaveFailurePublishesNeitherChange()
    {
        RecordingDataStore store = new()
        {
            SaveException = new IOException("simulated failure"),
        };
        GameManager manager = await CreateManagerAsync(store);
        DataEnvelope publishedBeforeAdd = manager.CurrentData;

        await Assert.ThrowsExactlyAsync<IOException>(
            () => manager.AddAsync(
                CreateDraft("First"),
                CancellationToken.None));

        Assert.AreEqual(1, store.SaveCount);
        Guid attemptedGameId = store.LastAttempted!.Games.Single().Id;
        Assert.AreEqual(
            attemptedGameId,
            store.LastAttempted.Settings.SelectedCompactGameId);
        Assert.AreEqual(publishedBeforeAdd, manager.CurrentData);
    }

    [TestMethod]
    public async Task AddAsync_MissingSelectionUsesFirstRegisteredGame()
    {
        RecordingDataStore store = new();
        GameEntry first = CreateEntry(Guid.NewGuid(), "First", 0);
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            SelectedCompactGameId = null,
        };
        GameManager manager = await CreateManagerAsync(
            store,
            settings,
            first);

        GameEntry added = await manager.AddAsync(
            CreateDraft("Added"),
            CancellationToken.None);

        Assert.AreNotEqual(first.Id, added.Id);
        Assert.AreEqual(
            first.Id,
            store.LastSaved!.Settings.SelectedCompactGameId);
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
        GameManager manager = await CreateManagerAsync(store, original);
        GameDraft initialDraft = new(
            original.Name,
            CurrentStamina: 44,
            original.MaxStamina,
            original.RecoveryMinutes,
            original.ImageAssetId,
            RecoverySeconds: 0,
            IsNotificationEnabled: true);
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
        GameManager manager = await CreateManagerAsync(store, original);
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
        GameManager manager = await CreateManagerAsync(
            store,
            first,
            second,
            third);

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
    public async Task DeleteAsync_SelectedGameSavesFallbackWithGamesOnce()
    {
        RecordingDataStore store = new();
        GameEntry first = CreateEntry(Guid.NewGuid(), "First", 0);
        GameEntry selected = CreateEntry(Guid.NewGuid(), "Selected", 1);
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            SelectedCompactGameId = selected.Id,
        };
        GameManager manager = await CreateManagerAsync(
            store,
            settings,
            first,
            selected);

        bool deleted = await manager.DeleteAsync(
            selected.Id,
            CancellationToken.None);

        Assert.IsTrue(deleted);
        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual(first.Id, store.LastSaved!.Games.Single().Id);
        Assert.AreEqual(
            first.Id,
            store.LastSaved.Settings.SelectedCompactGameId);
        Assert.AreEqual(store.LastSaved, manager.CurrentData);
    }

    [TestMethod]
    public async Task DeleteAsync_FallbackSaveFailurePublishesNeitherChange()
    {
        RecordingDataStore store = new()
        {
            SaveException = new IOException("simulated failure"),
        };
        GameEntry first = CreateEntry(Guid.NewGuid(), "First", 0);
        GameEntry selected = CreateEntry(Guid.NewGuid(), "Selected", 1);
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            SelectedCompactGameId = selected.Id,
        };
        GameManager manager = await CreateManagerAsync(
            store,
            settings,
            first,
            selected);
        DataEnvelope publishedBeforeDelete = manager.CurrentData;

        await Assert.ThrowsExactlyAsync<IOException>(
            () => manager.DeleteAsync(
                selected.Id,
                CancellationToken.None));

        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual(first.Id, store.LastAttempted!.Games.Single().Id);
        Assert.AreEqual(
            first.Id,
            store.LastAttempted.Settings.SelectedCompactGameId);
        Assert.AreEqual(publishedBeforeDelete, manager.CurrentData);
    }

    [TestMethod]
    public async Task DeleteAsync_MissingGameDoesNotPersist()
    {
        RecordingDataStore store = new();
        GameManager manager = await CreateManagerAsync(
            store,
            CreateEntry(Guid.NewGuid(), "Only", 0));

        bool deleted = await manager.DeleteAsync(
            Guid.NewGuid(),
            CancellationToken.None);

        Assert.IsFalse(deleted);
        Assert.AreEqual(0, store.SaveCount);
    }

    [TestMethod]
    public async Task UpdateSettingsAsync_SaveFailureKeepsPublishedSettings()
    {
        RecordingDataStore store = new()
        {
            SaveException = new IOException("simulated failure"),
        };
        GameManager manager = await CreateManagerAsync(store);
        AppSettings original = manager.CurrentData.Settings;

        await Assert.ThrowsExactlyAsync<IOException>(
            () => manager.UpdateSettingsAsync(
                settings => settings with
                {
                    LastDisplayMode = AppDisplayMode.Compact,
                },
                CancellationToken.None));

        Assert.AreEqual(original, manager.CurrentData.Settings);
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
        GameManager manager = await CreateManagerAsync(store, games);

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
        GameManager manager = await CreateManagerAsync(store, original);
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

    [TestMethod]
    public async Task AddAsync_ThrowingSubscriberDoesNotFailCommittedMutation()
    {
        RecordingDataStore store = new();
        GameManager manager = await CreateManagerAsync(store);
        bool secondSubscriberWasCalled = false;
        manager.GamesChanged += _ =>
            throw new InvalidOperationException("subscriber failure");
        manager.GamesChanged += _ =>
        {
            secondSubscriberWasCalled = true;
            return Task.CompletedTask;
        };

        GameEntry added = await manager.AddAsync(
            CreateDraft("Committed once"),
            CancellationToken.None);

        Assert.AreEqual(added, manager.Games.Single());
        Assert.AreEqual(1, store.SaveCount);
        Assert.IsTrue(secondSubscriberWasCalled);
        Assert.IsInstanceOfType<InvalidOperationException>(
            manager.LastNotificationError);
    }

    [TestMethod]
    public async Task OperationsBeforeInitializationAreRejected()
    {
        RecordingDataStore store = new()
        {
            PromotionResult = new RecoveryPromotionResult(
                CreateEnvelope(),
                "primary",
                "recovery",
                DiagnosticBackupPath: null),
        };
        GameManager manager = CreateUninitializedManager(store);
        GameEntry original = CreateEntry(Guid.NewGuid(), "Original", 0);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.AddAsync(
                CreateDraft("Not ready"),
                CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.EditAsync(
                original.Id,
                CreateDraft("Original"),
                CreateDraft("Edited"),
                CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.DeleteAsync(
                original.Id,
                CancellationToken.None));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.PromoteRecoveryAsync(CancellationToken.None));

        Assert.IsFalse(manager.IsInitialized);
        Assert.AreEqual(0, store.SaveCount);
        Assert.AreEqual(0, store.PromoteCount);
    }

    [TestMethod]
    public async Task InitializeAsync_SecondCallPreservesSavedState()
    {
        RecordingDataStore store = new();
        GameManager manager = await CreateManagerAsync(store);
        await manager.AddAsync(
            CreateDraft("Saved"),
            CancellationToken.None);
        DataEnvelope saved = store.LastSaved!;

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => manager.InitializeAsync(
                CreateEnvelope(
                    CreateEntry(Guid.NewGuid(), "Replacement", 0)),
                CancellationToken.None));

        Assert.AreEqual(saved, manager.CurrentData);
        Assert.AreEqual(saved, store.LastSaved);
    }

    [TestMethod]
    public async Task PromoteRecoveryAsync_WaitsForInFlightMutation()
    {
        RecordingDataStore store = new() { ShouldBlockSave = true };
        GameEntry original = CreateEntry(Guid.NewGuid(), "Original", 0);
        GameEntry recovered = CreateEntry(Guid.NewGuid(), "Recovered", 0);
        store.PromotionResult = new RecoveryPromotionResult(
            CreateEnvelope(recovered),
            "primary",
            "recovery",
            DiagnosticBackupPath: null);
        GameManager manager = await CreateManagerAsync(store, original);
        Task<GameEntry> addTask = manager.AddAsync(
            CreateDraft("In flight"),
            CancellationToken.None);
        await store.SaveStarted;

        Task<RecoveryPromotionResult> promoteTask =
            manager.PromoteRecoveryAsync(CancellationToken.None);

        Assert.AreEqual(0, store.PromoteCount);
        store.ReleaseSave();
        await addTask;
        await promoteTask;
        Assert.AreEqual(1, store.PromoteCount);
        Assert.AreEqual("Recovered", manager.Games.Single().Name);
        CollectionAssert.AreEqual(
            manager.Games,
            store.LastSaved!.Games);
        Assert.AreEqual(
            manager.CurrentData.Settings,
            store.LastSaved.Settings);
    }

    private static async Task<GameManager> CreateManagerAsync(
        RecordingDataStore store,
        params GameEntry[] games)
    {
        FakeClock clock = new(NowUtc);
        GameManager manager = CreateUninitializedManager(store, clock);
        await manager.InitializeAsync(
            CreateEnvelope(games),
            CancellationToken.None);
        return manager;
    }

    private static async Task<GameManager> CreateManagerAsync(
        RecordingDataStore store,
        AppSettings settings,
        params GameEntry[] games)
    {
        FakeClock clock = new(NowUtc);
        GameManager manager = CreateUninitializedManager(store, clock);
        await manager.InitializeAsync(
            CreateEnvelope(settings, games),
            CancellationToken.None);
        return manager;
    }

    private static GameManager CreateUninitializedManager(
        RecordingDataStore store,
        FakeClock? clock = null) => new(
            store,
            clock ?? new FakeClock(NowUtc),
            AppSettings.CreateDefault(AppTheme.Light));

    private static DataEnvelope CreateEnvelope(
        params GameEntry[] games) => new(
        DataEnvelope.CurrentSchemaVersion,
        games.ToImmutableArray(),
        AppSettings.CreateDefault(AppTheme.Light));

    private static DataEnvelope CreateEnvelope(
        AppSettings settings,
        params GameEntry[] games) => new(
        DataEnvelope.CurrentSchemaVersion,
        games.ToImmutableArray(),
        settings);

    private static GameDraft CreateDraft(string name) => new(
        name,
        CurrentStamina: 40,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        ImageAssetId: null,
        RecoverySeconds: 0,
        IsNotificationEnabled: true);

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
            sortOrder,
            RecoverySeconds: 0,
            IsNotificationEnabled: true);

    private sealed class RecordingDataStore : ILocalDataStore
    {
        private readonly TaskCompletionSource _saveStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _continueSave = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public DataEnvelope? LastSaved { get; private set; }

        public DataEnvelope? LastAttempted { get; private set; }

        public int SaveCount { get; private set; }

        public Exception? SaveException { get; init; }

        public bool ShouldBlockSave { get; init; }

        public Task SaveStarted => _saveStarted.Task;

        public RecoveryPromotionResult? PromotionResult { get; set; }

        public int PromoteCount { get; private set; }

        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task SaveAsync(
            DataEnvelope envelope,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            LastAttempted = envelope;
            if (ShouldBlockSave)
            {
                _saveStarted.TrySetResult();
                await _continueSave.Task.WaitAsync(cancellationToken);
            }

            if (SaveException is not null)
            {
                throw SaveException;
            }

            LastSaved = envelope;
        }

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PromoteCount++;
            RecoveryPromotionResult result =
                PromotionResult ?? throw new NotSupportedException();
            LastSaved = result.Envelope;
            return Task.FromResult(result);
        }

        public void ReleaseSave() => _continueSave.TrySetResult();
    }
}
