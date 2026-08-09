using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Tests.TestDoubles;

namespace StaminaManager.Tests.Notifications;

[TestClass]
public sealed class NotificationCoordinatorTests
{
    private static readonly DateTimeOffset RecordedAtUtc = new(
        2026,
        7,
        25,
        0,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public async Task ReconcileAsync_SavesScheduledOnlyAfterPlatformAccepts()
    {
        GameEntry game = CreateGame(Guid.NewGuid(), baseStamina: 90);
        FakeNotificationScheduler scheduler = new();
        FakeNotificationLedgerStore ledger = new();
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(5));

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [game],
            CreateSettings(enabled: true, leadMinutes: 15),
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        CollectionAssert.AreEqual(
            new[] { $"schedule:{game.Id:N}" },
            scheduler.Operations);
        Assert.HasCount(1, ledger.SavedEntries);
        Assert.AreEqual(
            NotificationState.Scheduled,
            ledger.SavedEntries[0].State);
        Assert.IsTrue(
            scheduler.AcceptedAtUtc <= ledger.SavedAtUtc,
            "台帳はWindowsが予約を受理した後に保存する必要があります。");
    }

    [TestMethod]
    public async Task ReconcileAsync_ScheduleFailureKeepsPreviousLedger()
    {
        GameEntry game = CreateGame(Guid.NewGuid(), baseStamina: 90);
        FakeNotificationScheduler scheduler = new()
        {
            ScheduleFailure = new InvalidOperationException("denied"),
        };
        NotificationLedgerEntry previous = CreateLedger(
            game,
            leadMinutes: 20,
            NotificationState.Scheduled);
        FakeNotificationLedgerStore ledger = new([previous]);
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(5));

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [game],
            CreateSettings(enabled: true, leadMinutes: 15),
            CancellationToken.None);

        Assert.IsTrue(result.HasFailures);
        Assert.HasCount(1, result.Issues);
        Assert.HasCount(1, ledger.SavedEntries);
        Assert.AreSame(previous, ledger.SavedEntries[0]);
    }

    [TestMethod]
    public async Task ReconcileAsync_SameCycleLanguageChangeReplacesScheduledNotification()
    {
        GameEntry game = CreateGame(Guid.NewGuid(), baseStamina: 90);
        FakeNotificationScheduler scheduler = new();
        FakeNotificationLedgerStore ledger = new();
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(5));
        AppSettings japaneseSettings = CreateSettings(
            enabled: true,
            leadMinutes: 15) with
        {
            Language = AppLanguage.Japanese,
        };

        await coordinator.ReconcileAsync(
            [game],
            japaneseSettings,
            CancellationToken.None);
        NotificationLedgerEntry japaneseEntry = ledger.SavedEntries.Single();

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [game],
            japaneseSettings with { Language = AppLanguage.English },
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        CollectionAssert.AreEqual(
            new[]
            {
                $"schedule:{game.Id:N}",
                $"cancel:{game.Id:N}",
                $"schedule:{game.Id:N}",
            },
            scheduler.Operations);
        NotificationLedgerEntry englishEntry = ledger.SavedEntries.Single();
        Assert.AreEqual(japaneseEntry.Key, englishEntry.Key);
        Assert.AreNotEqual(
            japaneseEntry.ContentFingerprint,
            englishEntry.ContentFingerprint);
    }

    [TestMethod]
    public async Task ReconcileAsync_ImmediateSubmissionPersistsConsumedAfterShow()
    {
        GameEntry game = CreateGame(Guid.NewGuid(), baseStamina: 90);
        FakeNotificationScheduler scheduler = new();
        FakeNotificationLedgerStore ledger = new();
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(40));

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [game],
            CreateSettings(enabled: true, leadMinutes: 15),
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        CollectionAssert.AreEqual(
            new[] { $"show:{game.Id:N}" },
            scheduler.Operations);
        Assert.AreEqual(
            NotificationState.Consumed,
            ledger.SavedEntries.Single().State);
        Assert.IsTrue(scheduler.AcceptedAtUtc <= ledger.SavedAtUtc);
    }

    [TestMethod]
    public async Task ReconcileAsync_PersistsAcceptedNotificationBeforeLaterCancellation()
    {
        GameEntry accepted = CreateGame(Guid.NewGuid(), baseStamina: 90);
        GameEntry cancelled = CreateGame(Guid.NewGuid(), baseStamina: 80);
        FakeNotificationScheduler scheduler = new()
        {
            CancellationGameId = cancelled.Id,
        };
        FakeNotificationLedgerStore ledger = new();
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(40));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => coordinator.ReconcileAsync(
                [accepted, cancelled],
                CreateSettings(enabled: true, leadMinutes: 15),
                CancellationToken.None));

        NotificationLedgerEntry persisted = ledger.SavedEntries.Single();
        Assert.AreEqual(accepted.Id, persisted.GameId);
        Assert.AreEqual(NotificationState.Consumed, persisted.State);
    }

    [TestMethod]
    public async Task ReconcileAsync_PersistsAcceptedFutureScheduleBeforeLaterCancellation()
    {
        GameEntry accepted = CreateGame(Guid.NewGuid(), baseStamina: 80);
        GameEntry cancelled = CreateGame(Guid.NewGuid(), baseStamina: 70);
        FakeNotificationScheduler scheduler = new()
        {
            CancellationGameId = cancelled.Id,
        };
        FakeNotificationLedgerStore ledger = new();
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(5));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => coordinator.ReconcileAsync(
                [accepted, cancelled],
                CreateSettings(enabled: true, leadMinutes: 15),
                CancellationToken.None));

        NotificationLedgerEntry persisted = ledger.SavedEntries.Single();
        Assert.AreEqual(accepted.Id, persisted.GameId);
        Assert.AreEqual(NotificationState.Scheduled, persisted.State);
        Assert.AreEqual(1, ledger.SaveCount);
    }

    [TestMethod]
    public async Task ReconcileAsync_PrunesOldCyclesBeforeLedgerLimit()
    {
        GameEntry game = CreateGame(Guid.NewGuid(), baseStamina: 90);
        IReadOnlyList<NotificationLedgerEntry> oldEntries = Enumerable
            .Range(1, 1_000)
            .Select(index => NotificationLedgerEntry.Create(
                game,
                RecordedAtUtc.AddTicks(-index),
                leadMinutes: 0,
                NotificationState.Consumed))
            .ToArray();
        FakeNotificationScheduler scheduler = new();
        FakeNotificationLedgerStore ledger = new(oldEntries);
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(40));

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [game],
            CreateSettings(enabled: true, leadMinutes: 15),
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        Assert.HasCount(1_000, ledger.SavedEntries);
        Assert.IsTrue(ledger.SavedEntries.Any(entry =>
            entry.GameId == game.Id
            && entry.LeadMinutes == 15
            && entry.State == NotificationState.Consumed));
    }

    [TestMethod]
    public async Task ReconcileAsync_BatchesReconstructableScheduleLedgerWrites()
    {
        GameEntry[] games = Enumerable.Range(0, 100)
            .Select(index => CreateGame(
                Guid.NewGuid(),
                baseStamina: 80) with
            {
                SortOrder = index,
            })
            .ToArray();
        FakeNotificationScheduler scheduler = new();
        FakeNotificationLedgerStore ledger = new();
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(5));

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            games,
            CreateSettings(enabled: true, leadMinutes: 15),
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        Assert.AreEqual(1, ledger.SaveCount);
        Assert.HasCount(100, ledger.SavedEntries);
    }

    [TestMethod]
    public async Task ReconcileAsync_DisabledCancelsAllAndSuppressesCycles()
    {
        GameEntry first = CreateGame(Guid.NewGuid(), baseStamina: 90);
        GameEntry second = CreateGame(
            Guid.NewGuid(),
            baseStamina: 80) with
        {
            IsNotificationEnabled = false,
        };
        FakeNotificationScheduler scheduler = new(
            scheduledGameIds: new HashSet<Guid> { first.Id, second.Id });
        FakeNotificationLedgerStore ledger = new(
            [
                CreateLedger(first, 15, NotificationState.Scheduled),
                CreateLedger(second, 15, NotificationState.Scheduled),
            ]);
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(5));

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [first, second],
            CreateSettings(enabled: false, leadMinutes: 15),
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        CollectionAssert.AreEqual(
            new[] { "cancel-all" },
            scheduler.Operations);
        Assert.IsTrue(ledger.SavedEntries.All(
            entry => entry.State == NotificationState.Suppressed));
    }

    [TestMethod]
    public async Task ReconcileAsync_EnabledHonorsIndividualNotificationState()
    {
        GameEntry disabled = CreateGame(
            Guid.NewGuid(),
            baseStamina: 90) with
        {
            IsNotificationEnabled = false,
        };
        GameEntry enabled = CreateGame(Guid.NewGuid(), baseStamina: 80);
        FakeNotificationLedgerStore ledger = new(
            [CreateLedger(disabled, 15, NotificationState.Scheduled)]);
        bool wasSuppressionSavedBeforeSchedule = false;
        FakeNotificationScheduler scheduler = new(
            new HashSet<Guid> { disabled.Id })
        {
            ScheduleObserved = _ =>
            {
                wasSuppressionSavedBeforeSchedule =
                    ledger.SavedEntries.Any(entry =>
                        entry.GameId == disabled.Id
                        && entry.State == NotificationState.Suppressed);
            },
        };
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(5));

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [disabled, enabled],
            CreateSettings(enabled: true, leadMinutes: 15),
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        CollectionAssert.AreEqual(
            new[]
            {
                $"cancel:{disabled.Id:N}",
                $"schedule:{enabled.Id:N}",
            },
            scheduler.Operations);
        Assert.IsTrue(wasSuppressionSavedBeforeSchedule);
        Assert.AreEqual(
            NotificationState.Suppressed,
            ledger.SavedEntries.Single(entry =>
                entry.GameId == disabled.Id).State);
        Assert.AreEqual(
            NotificationState.Scheduled,
            ledger.SavedEntries.Single(entry =>
                entry.GameId == enabled.Id).State);
    }

    [TestMethod]
    public async Task ReconcileAsync_IndividualDisabledWithoutWindowsScheduleSuppressesLedger()
    {
        GameEntry valid = CreateGame(Guid.NewGuid(), baseStamina: 90);
        NotificationLedgerEntry scheduled = CreateLedger(
            valid,
            leadMinutes: 15,
            NotificationState.Scheduled);
        GameEntry uncalculable = valid with
        {
            BaseStamina = 99,
            RecordedAtUtc = DateTimeOffset.MaxValue,
            RecoveryMinutes = 1,
            IsNotificationEnabled = false,
        };
        FakeNotificationScheduler scheduler = new();
        FakeNotificationLedgerStore ledger = new([scheduled]);
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: DateTimeOffset.MaxValue);

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [uncalculable],
            CreateSettings(enabled: true, leadMinutes: 15),
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        Assert.IsEmpty(scheduler.Operations);
        Assert.AreEqual(1, ledger.SaveCount);
        Assert.AreEqual(
            NotificationState.Suppressed,
            ledger.SavedEntries.Single().State);
        Assert.AreEqual(scheduled.Key, ledger.SavedEntries.Single().Key);
    }

    [TestMethod]
    public async Task ReconcileAsync_GameChangeReevaluatesIndividualState()
    {
        GameEntry enabled = CreateGame(Guid.NewGuid(), baseStamina: 90);
        GameEntry disabled = enabled with
        {
            IsNotificationEnabled = false,
        };
        FakeNotificationScheduler scheduler = new();
        FakeNotificationLedgerStore ledger = new();
        FakeClock clock = new(RecordedAtUtc.AddMinutes(5));
        NotificationCoordinator coordinator = new(scheduler, ledger, clock);
        AppSettings settings = CreateSettings(
            enabled: true,
            leadMinutes: 15);

        await coordinator.ReconcileAsync(
            [enabled],
            settings,
            CancellationToken.None);
        await coordinator.ReconcileAsync(
            [disabled],
            settings,
            CancellationToken.None);
        await coordinator.ReconcileAsync(
            [enabled],
            settings,
            CancellationToken.None);

        CollectionAssert.AreEqual(
            new[]
            {
                $"schedule:{enabled.Id:N}",
                $"cancel:{enabled.Id:N}",
                $"schedule:{enabled.Id:N}",
            },
            scheduler.Operations);
        Assert.HasCount(1, ledger.SavedEntries);
        Assert.AreEqual(
            NotificationState.Scheduled,
            ledger.SavedEntries.Single().State);
    }

    [TestMethod]
    public async Task ReconcileAsync_ReenabledPastDueConsumesWithoutShowing()
    {
        GameEntry enabled = CreateGame(Guid.NewGuid(), baseStamina: 90);
        GameEntry disabled = enabled with
        {
            IsNotificationEnabled = false,
        };
        FakeNotificationScheduler scheduler = new();
        FakeNotificationLedgerStore ledger = new();
        FakeClock clock = new(RecordedAtUtc.AddMinutes(5));
        NotificationCoordinator coordinator = new(scheduler, ledger, clock);
        AppSettings settings = CreateSettings(
            enabled: true,
            leadMinutes: 15);

        await coordinator.ReconcileAsync(
            [disabled],
            settings,
            CancellationToken.None);
        clock.UtcNow = RecordedAtUtc.AddMinutes(40);
        await coordinator.ReconcileAsync(
            [enabled],
            settings,
            CancellationToken.None);

        Assert.IsEmpty(scheduler.Operations);
        Assert.AreEqual(
            NotificationState.Consumed,
            ledger.SavedEntries.Single().State);
    }

    [TestMethod]
    public async Task ReconcileAsync_MissingDueScheduleConsumesWithoutShowing()
    {
        GameEntry game = CreateGame(Guid.NewGuid(), baseStamina: 90);
        NotificationLedgerEntry scheduled = CreateLedger(
            game,
            15,
            NotificationState.Scheduled);
        FakeNotificationScheduler scheduler = new();
        FakeNotificationLedgerStore ledger = new([scheduled]);
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(35));

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [game],
            CreateSettings(enabled: true, leadMinutes: 15),
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        Assert.IsEmpty(scheduler.Operations);
        Assert.AreEqual(
            NotificationState.Consumed,
            ledger.SavedEntries.Single().State);
    }

    [TestMethod]
    public async Task ReconcileAsync_PartialFailureContinuesOtherGames()
    {
        GameEntry failed = CreateGame(Guid.NewGuid(), baseStamina: 90);
        GameEntry accepted = CreateGame(Guid.NewGuid(), baseStamina: 80);
        FakeNotificationScheduler scheduler = new()
        {
            FailingGameId = failed.Id,
        };
        FakeNotificationLedgerStore ledger = new();
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(5));

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [failed, accepted],
            CreateSettings(enabled: true, leadMinutes: 15),
            CancellationToken.None);

        Assert.IsTrue(result.HasFailures);
        Assert.HasCount(1, result.Issues);
        CollectionAssert.Contains(
            scheduler.Operations,
            $"schedule:{accepted.Id:N}");
        Assert.IsTrue(ledger.SavedEntries.Any(
            entry => entry.GameId == accepted.Id));
        Assert.IsFalse(ledger.SavedEntries.Any(
            entry => entry.GameId == failed.Id));
    }

    [TestMethod]
    public async Task ReconcileAsync_InvalidSchedulePreservesPriorSchedule()
    {
        GameEntry valid = CreateGame(Guid.NewGuid(), baseStamina: 90);
        NotificationLedgerEntry previous = CreateLedger(
            valid,
            15,
            NotificationState.Scheduled);
        GameEntry underflow = valid with
        {
            BaseStamina = 99,
            RecoveryMinutes = 1,
            RecordedAtUtc = DateTimeOffset.MinValue,
        };
        FakeNotificationScheduler scheduler = new(
            new HashSet<Guid> { valid.Id });
        FakeNotificationLedgerStore ledger = new([previous]);
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: DateTimeOffset.MinValue);

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [underflow],
            CreateSettings(enabled: true, leadMinutes: 2),
            CancellationToken.None);

        Assert.IsTrue(result.HasInvalidSchedule);
        Assert.IsEmpty(scheduler.Operations);
        Assert.AreSame(previous, ledger.SavedEntries.Single());
    }

    [TestMethod]
    public async Task ReconcileAsync_DisabledInvalidScheduleSuppressesWithoutFailure()
    {
        GameEntry valid = CreateGame(Guid.NewGuid(), baseStamina: 90);
        NotificationLedgerEntry previous = CreateLedger(
            valid,
            15,
            NotificationState.Scheduled);
        GameEntry underflow = valid with
        {
            BaseStamina = 99,
            RecoveryMinutes = 1,
            RecordedAtUtc = DateTimeOffset.MinValue,
        };
        FakeNotificationScheduler scheduler = new(
            new HashSet<Guid> { valid.Id });
        FakeNotificationLedgerStore ledger = new([previous]);
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: DateTimeOffset.MinValue);

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [underflow],
            CreateSettings(enabled: false, leadMinutes: 2),
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        CollectionAssert.AreEqual(
            new[] { "cancel-all" },
            scheduler.Operations);
        Assert.AreEqual(
            NotificationState.Suppressed,
            ledger.SavedEntries.Single().State);
    }

    [TestMethod]
    public async Task ReconcileAsync_DeletedGameCancelsOrphanedSchedule()
    {
        GameEntry deleted = CreateGame(Guid.NewGuid(), baseStamina: 90);
        FakeNotificationScheduler scheduler = new(
            new HashSet<Guid> { deleted.Id });
        FakeNotificationLedgerStore ledger = new(
            [CreateLedger(deleted, 15, NotificationState.Scheduled)]);
        NotificationCoordinator coordinator = CreateCoordinator(
            scheduler,
            ledger,
            nowUtc: RecordedAtUtc.AddMinutes(5));

        NotificationReconcileResult result = await coordinator.ReconcileAsync(
            [],
            CreateSettings(enabled: true, leadMinutes: 15),
            CancellationToken.None);

        Assert.IsFalse(result.HasFailures);
        CollectionAssert.AreEqual(
            new[] { $"cancel:{deleted.Id:N}" },
            scheduler.Operations);
        Assert.IsEmpty(ledger.SavedEntries);
    }

    private static NotificationCoordinator CreateCoordinator(
        FakeNotificationScheduler scheduler,
        FakeNotificationLedgerStore ledger,
        DateTimeOffset nowUtc) => new(
            scheduler,
            ledger,
            new FakeClock(nowUtc));

    private static AppSettings CreateSettings(
        bool enabled,
        int leadMinutes) => AppSettings.CreateDefault(AppTheme.Light) with
        {
            NotificationsEnabled = enabled,
            NotificationLeadMinutes = leadMinutes,
        };

    private static NotificationLedgerEntry CreateLedger(
        GameEntry game,
        int leadMinutes,
        NotificationState state) => NotificationLedgerEntry.Create(
            game,
            game.RecordedAtUtc.AddMinutes(
                (game.MaxStamina - game.BaseStamina)
                * game.RecoveryMinutes),
            leadMinutes,
            state);

    private static GameEntry CreateGame(Guid id, int baseStamina) => new(
        id,
        "Test game",
        baseStamina,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        RecordedAtUtc,
        ImageAssetId: null,
        SortOrder: 0,
        RecoverySeconds: 0,
        IsNotificationEnabled: true);

    private sealed class FakeNotificationLedgerStore(
        IReadOnlyList<NotificationLedgerEntry>? initialEntries = null)
        : INotificationLedgerStore
    {
        private IReadOnlyList<NotificationLedgerEntry> _entries =
            initialEntries ?? [];

        public IReadOnlyList<NotificationLedgerEntry> SavedEntries =>
            _entries;

        public DateTimeOffset SavedAtUtc { get; private set; }

        public int SaveCount { get; private set; }

        public Task<IReadOnlyList<NotificationLedgerEntry>> LoadAsync(
            CancellationToken cancellationToken) => Task.FromResult(_entries);

        public Task SaveAsync(
            IReadOnlyCollection<NotificationLedgerEntry> entries,
            CancellationToken cancellationToken)
        {
            _entries = entries.ToArray();
            SaveCount++;
            SavedAtUtc = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeNotificationScheduler(
        IReadOnlySet<Guid>? scheduledGameIds = null)
        : INotificationScheduler
    {
        private readonly HashSet<Guid> _scheduledGameIds =
            scheduledGameIds?.ToHashSet() ?? [];

        public List<string> Operations { get; } = [];

        public Exception? ScheduleFailure { get; init; }

        public Guid? FailingGameId { get; init; }

        public Guid? CancellationGameId { get; init; }

        public Action<NotificationRequest>? ScheduleObserved { get; init; }

        public DateTimeOffset AcceptedAtUtc { get; private set; }

        public event EventHandler<NotificationActivationEventArgs>?
            ActivationRequested
        {
            add { }
            remove { }
        }

        public void Initialize()
        {
        }

        public void Dispose()
        {
        }

        public Task<IReadOnlySet<Guid>> GetScheduledGameIdsAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                (IReadOnlySet<Guid>)_scheduledGameIds);

        public Task ScheduleAsync(
            NotificationRequest request,
            CancellationToken cancellationToken)
        {
            ScheduleObserved?.Invoke(request);
            if (CancellationGameId == request.GameId)
            {
                throw new OperationCanceledException();
            }

            if (ScheduleFailure is not null)
            {
                throw ScheduleFailure;
            }

            if (FailingGameId == request.GameId)
            {
                throw new InvalidOperationException("schedule failed");
            }

            Operations.Add($"schedule:{request.GameId:N}");
            _scheduledGameIds.Add(request.GameId);
            AcceptedAtUtc = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task ShowImmediateAsync(
            NotificationRequest request,
            CancellationToken cancellationToken)
        {
            Operations.Add($"show:{request.GameId:N}");
            AcceptedAtUtc = DateTimeOffset.UtcNow;
            return Task.CompletedTask;
        }

        public Task CancelAsync(
            Guid gameId,
            CancellationToken cancellationToken)
        {
            Operations.Add($"cancel:{gameId:N}");
            _scheduledGameIds.Remove(gameId);
            return Task.CompletedTask;
        }

        public Task CancelAllAsync(CancellationToken cancellationToken)
        {
            Operations.Add("cancel-all");
            _scheduledGameIds.Clear();
            return Task.CompletedTask;
        }
    }
}
