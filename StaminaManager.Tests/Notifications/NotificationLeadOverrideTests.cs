using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Tests.TestDoubles;

namespace StaminaManager.Tests.Notifications;

[TestClass]
public sealed class NotificationLeadOverrideTests
{
    private static readonly DateTimeOffset RecordedAtUtc = new(
        2026,
        8,
        14,
        0,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public async Task ReconcileAsync_GameOverrideTakesPrecedenceOverGlobalLeadTime()
    {
        GameEntry game = CreateGame() with
        {
            NotificationLeadMinutesOverride = 30,
        };
        RecordingScheduler scheduler = new();
        RecordingLedgerStore ledger = new();
        NotificationCoordinator coordinator = new(
            scheduler,
            ledger,
            new FakeClock(RecordedAtUtc.AddMinutes(5)));
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            NotificationLeadMinutes = 15,
        };

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [game],
            settings,
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        NotificationRequest request = scheduler.ScheduledRequests.Single();
        Assert.AreEqual(RecordedAtUtc.AddMinutes(50), request.FullAtUtc);
        Assert.AreEqual(RecordedAtUtc.AddMinutes(20), request.NotificationAtUtc);
        Assert.AreEqual(30, ledger.SavedEntries.Single().LeadMinutes);
    }

    [TestMethod]
    public async Task ReconcileAsync_NullGameOverrideUsesGlobalLeadTime()
    {
        GameEntry game = CreateGame();
        RecordingScheduler scheduler = new();
        RecordingLedgerStore ledger = new();
        NotificationCoordinator coordinator = new(
            scheduler,
            ledger,
            new FakeClock(RecordedAtUtc.AddMinutes(5)));
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            NotificationLeadMinutes = 15,
        };

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [game],
            settings,
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        NotificationRequest request = scheduler.ScheduledRequests.Single();
        Assert.AreEqual(RecordedAtUtc.AddMinutes(35), request.NotificationAtUtc);
        Assert.AreEqual(15, ledger.SavedEntries.Single().LeadMinutes);
    }

    private static GameEntry CreateGame() => new(
        Guid.NewGuid(),
        "Test game",
        BaseStamina: 90,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        RecordedAtUtc,
        ImageAssetId: null,
        SortOrder: 0,
        RecoverySeconds: 0,
        IsNotificationEnabled: true);

    private sealed class RecordingScheduler : INotificationScheduler
    {
        public List<NotificationRequest> ScheduledRequests { get; } = [];

        public event EventHandler<NotificationActivationEventArgs>?
            ActivationRequested
        {
            add { }
            remove { }
        }

        public void Initialize()
        {
        }

        public Task<IReadOnlySet<Guid>> GetScheduledGameIdsAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task ScheduleAsync(
            NotificationRequest request,
            CancellationToken cancellationToken)
        {
            ScheduledRequests.Add(request);
            return Task.CompletedTask;
        }

        public Task ShowImmediateAsync(
            NotificationRequest request,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CancelAsync(
            Guid gameId,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task CancelAllAsync(
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLedgerStore : INotificationLedgerStore
    {
        public IReadOnlyList<NotificationLedgerEntry> SavedEntries { get; private set; }
            = [];

        public Task<IReadOnlyList<NotificationLedgerEntry>> LoadAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<NotificationLedgerEntry>>([]);

        public Task SaveAsync(
            IReadOnlyCollection<NotificationLedgerEntry> entries,
            CancellationToken cancellationToken)
        {
            SavedEntries = entries.ToArray();
            return Task.CompletedTask;
        }
    }
}
