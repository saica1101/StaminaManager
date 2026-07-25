using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Tests.TestDoubles;
using System.Collections.Immutable;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class AppCoordinatorTests
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
    public async Task InitializeAsync_SetsInitializedOnlyAfterReconciliation()
    {
        CoordinatorDataStore store = new(CreateLoadResult("Loaded"));
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(store, manager);
        using CancellationTokenSource cancellation = new();
        coordinator.NavigationRequested += (_, _) => cancellation.Cancel();

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => coordinator.InitializeAsync(cancellation.Token));

        Assert.IsFalse(coordinator.IsInitialized);
    }

    [TestMethod]
    public async Task PromoteRecoveryAsync_BeforeInitializationIsRejected()
    {
        CoordinatorDataStore store = new(CreateLoadResult("Loaded"))
        {
            PromotionResult = CreatePromotionResult("Recovered"),
        };
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(store, manager);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => coordinator.PromoteRecoveryAsync(
                CancellationToken.None));

        Assert.AreEqual(0, store.PromoteCount);
    }

    [TestMethod]
    public async Task PromoteRecoveryAsync_UsesGameManagerAtomicPromotion()
    {
        CoordinatorDataStore store = new(CreateLoadResult("Loaded"))
        {
            PromotionResult = CreatePromotionResult("Recovered"),
        };
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(store, manager);
        await coordinator.InitializeAsync(CancellationToken.None);

        await coordinator.PromoteRecoveryAsync(CancellationToken.None);

        Assert.AreEqual(1, store.PromoteCount);
        Assert.AreEqual("Recovered", manager.Games.Single().Name);
    }

    private static GameManager CreateManager(ILocalDataStore store) => new(
        store,
        new FakeClock(NowUtc),
        AppSettings.CreateDefault(AppTheme.Light));

    private static DataLoadResult CreateLoadResult(string gameName) => new(
        DataLoadStatus.Primary,
        CreateEnvelope(gameName),
        "primary",
        "recovery");

    private static RecoveryPromotionResult CreatePromotionResult(
        string gameName) => new(
            CreateEnvelope(gameName),
            "primary",
            "recovery",
            DiagnosticBackupPath: null);

    private static DataEnvelope CreateEnvelope(string gameName) => new(
        DataEnvelope.CurrentSchemaVersion,
        ImmutableArray.Create(new GameEntry(
            Guid.NewGuid(),
            gameName,
            BaseStamina: 40,
            MaxStamina: 100,
            RecoveryMinutes: 5,
            RecordedAtUtc: NowUtc,
            ImageAssetId: null,
            SortOrder: 0)),
        AppSettings.CreateDefault(AppTheme.Light));

    private sealed class CoordinatorDataStore(DataLoadResult loadResult)
        : ILocalDataStore
    {
        public RecoveryPromotionResult? PromotionResult { get; init; }

        public int PromoteCount { get; private set; }

        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult(loadResult);

        public Task SaveAsync(
            DataEnvelope envelope,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken)
        {
            PromoteCount++;
            return Task.FromResult(
                PromotionResult ?? throw new NotSupportedException());
        }
    }
}
