using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;

namespace StaminaManager.Tests.Notifications;

[TestClass]
public sealed class NotificationStateMachineTests
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
    public void Evaluate_FutureCycleSchedulesDeterministicKey()
    {
        GameEntry game = CreateGame(baseStamina: 90);
        DateTimeOffset nowUtc = RecordedAtUtc.AddMinutes(5);

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc,
            notificationsEnabled: true,
            existingEntry: null,
            isScheduledInWindows: false);

        Assert.AreEqual(NotificationPlatformAction.Schedule, decision.Action);
        Assert.AreEqual(NotificationDecisionError.None, decision.Error);
        Assert.IsNotNull(decision.Entry);
        Assert.AreEqual(NotificationState.Scheduled, decision.Entry.State);
        Assert.AreEqual(
            $"{game.Id:N}/{RecordedAtUtc.AddMinutes(50).UtcTicks}/15",
            decision.Entry.Key);
        Assert.AreEqual(
            RecordedAtUtc.AddMinutes(35).UtcTicks,
            decision.Entry.NotificationAtUtcTicks);
    }

    [TestMethod]
    public void Evaluate_PastDueButRecoveringShowsImmediateOnce()
    {
        GameEntry game = CreateGame(baseStamina: 90);

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc: RecordedAtUtc.AddMinutes(40),
            notificationsEnabled: true,
            existingEntry: null,
            isScheduledInWindows: false);

        Assert.AreEqual(
            NotificationPlatformAction.ShowImmediate,
            decision.Action);
        Assert.AreEqual(NotificationState.Consumed, decision.Entry?.State);
    }

    [TestMethod]
    [DataRow(100)]
    [DataRow(101)]
    public void Evaluate_FullOrOverCapSkipsNewNotification(int baseStamina)
    {
        GameEntry game = CreateGame(baseStamina);

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            RecordedAtUtc,
            notificationsEnabled: true,
            existingEntry: null,
            isScheduledInWindows: false);

        Assert.AreEqual(NotificationPlatformAction.None, decision.Action);
        Assert.IsNull(decision.Entry);
    }

    [TestMethod]
    public void Evaluate_MissingScheduledEntryAtDueMarksConsumed()
    {
        GameEntry game = CreateGame(baseStamina: 90);
        NotificationLedgerEntry scheduled = CreateLedger(
            game,
            leadMinutes: 15,
            NotificationState.Scheduled);

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc: RecordedAtUtc.AddMinutes(35),
            notificationsEnabled: true,
            scheduled,
            isScheduledInWindows: false);

        Assert.AreEqual(NotificationPlatformAction.None, decision.Action);
        Assert.AreEqual(NotificationState.Consumed, decision.Entry?.State);
    }

    [TestMethod]
    public void Evaluate_MissingScheduledEntryBeforeDueRegistersAgain()
    {
        GameEntry game = CreateGame(baseStamina: 90);
        NotificationLedgerEntry scheduled = CreateLedger(
            game,
            leadMinutes: 15,
            NotificationState.Scheduled);

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc: RecordedAtUtc.AddMinutes(10),
            notificationsEnabled: true,
            scheduled,
            isScheduledInWindows: false);

        Assert.AreEqual(
            NotificationPlatformAction.ReplaceScheduled,
            decision.Action);
        Assert.AreEqual(NotificationState.Scheduled, decision.Entry?.State);
    }

    [TestMethod]
    public void Evaluate_DisabledNotificationsCancelAndSuppress()
    {
        GameEntry game = CreateGame(baseStamina: 90);
        NotificationLedgerEntry scheduled = CreateLedger(
            game,
            leadMinutes: 15,
            NotificationState.Scheduled);

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc: RecordedAtUtc.AddMinutes(10),
            notificationsEnabled: false,
            scheduled,
            isScheduledInWindows: true);

        Assert.AreEqual(NotificationPlatformAction.Cancel, decision.Action);
        Assert.AreEqual(NotificationState.Suppressed, decision.Entry?.State);
    }

    [TestMethod]
    public void Evaluate_DisabledUncalculableCycleCancelsWindowsSchedule()
    {
        GameEntry game = CreateGame(baseStamina: 99) with
        {
            RecordedAtUtc = DateTimeOffset.MaxValue,
            RecoveryMinutes = 1,
        };

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc: DateTimeOffset.MaxValue,
            notificationsEnabled: false,
            existingEntry: null,
            isScheduledInWindows: true);

        Assert.AreEqual(NotificationPlatformAction.Cancel, decision.Action);
        Assert.AreEqual(NotificationDecisionError.None, decision.Error);
        Assert.IsNull(decision.Entry);
    }

    [TestMethod]
    public void Evaluate_DisabledUncalculableCycleSuppressesScheduledLedger()
    {
        GameEntry validGame = CreateGame(baseStamina: 90);
        NotificationLedgerEntry scheduled = CreateLedger(
            validGame,
            leadMinutes: 15,
            NotificationState.Scheduled);
        GameEntry game = validGame with
        {
            BaseStamina = 99,
            RecordedAtUtc = DateTimeOffset.MaxValue,
            RecoveryMinutes = 1,
        };

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc: DateTimeOffset.MaxValue,
            notificationsEnabled: false,
            scheduled,
            isScheduledInWindows: false);

        Assert.AreEqual(NotificationPlatformAction.None, decision.Action);
        Assert.AreEqual(NotificationDecisionError.None, decision.Error);
        Assert.AreEqual(NotificationState.Suppressed, decision.Entry?.State);
        Assert.AreEqual(scheduled.Key, decision.Entry?.Key);
    }

    [TestMethod]
    [DataRow(NotificationState.Scheduled)]
    [DataRow(NotificationState.Suppressed)]
    [DataRow(NotificationState.Consumed)]
    public void Evaluate_DisabledWindowsScheduleAlwaysCancels(
        NotificationState ledgerState)
    {
        GameEntry game = CreateGame(baseStamina: 90);
        NotificationLedgerEntry existing = CreateLedger(
            game,
            leadMinutes: 15,
            ledgerState);

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc: RecordedAtUtc.AddMinutes(5),
            notificationsEnabled: false,
            existing,
            isScheduledInWindows: true);

        Assert.AreEqual(NotificationPlatformAction.Cancel, decision.Action);
    }

    [TestMethod]
    public void Evaluate_DisabledNewCycleCreatesSuppressedLedger()
    {
        GameEntry game = CreateGame(baseStamina: 90);

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc: RecordedAtUtc.AddMinutes(5),
            notificationsEnabled: false,
            existingEntry: null,
            isScheduledInWindows: false);

        Assert.AreEqual(NotificationPlatformAction.None, decision.Action);
        Assert.AreEqual(NotificationDecisionError.None, decision.Error);
        Assert.AreEqual(NotificationState.Suppressed, decision.Entry?.State);
    }

    [TestMethod]
    public void Evaluate_ReenabledBeforeDueSchedulesSuppressedCycle()
    {
        GameEntry game = CreateGame(baseStamina: 90);
        NotificationLedgerEntry suppressed = CreateLedger(
            game,
            leadMinutes: 15,
            NotificationState.Suppressed);

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc: RecordedAtUtc.AddMinutes(10),
            notificationsEnabled: true,
            suppressed,
            isScheduledInWindows: false);

        Assert.AreEqual(NotificationPlatformAction.Schedule, decision.Action);
        Assert.AreEqual(NotificationState.Scheduled, decision.Entry?.State);
    }

    [TestMethod]
    public void Evaluate_ReenabledAfterDueConsumesWithoutShowing()
    {
        GameEntry game = CreateGame(baseStamina: 90);
        NotificationLedgerEntry suppressed = CreateLedger(
            game,
            leadMinutes: 15,
            NotificationState.Suppressed);

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc: RecordedAtUtc.AddMinutes(40),
            notificationsEnabled: true,
            suppressed,
            isScheduledInWindows: false);

        Assert.AreEqual(NotificationPlatformAction.None, decision.Action);
        Assert.AreEqual(NotificationState.Consumed, decision.Entry?.State);
    }

    [TestMethod]
    public void Evaluate_NameOnlyChangeReplacesScheduleWithSameKey()
    {
        GameEntry original = CreateGame(baseStamina: 90);
        NotificationLedgerEntry scheduled = CreateLedger(
            original,
            leadMinutes: 15,
            NotificationState.Scheduled);
        GameEntry renamed = original with { Name = "Renamed game" };

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            renamed,
            leadMinutes: 15,
            nowUtc: RecordedAtUtc.AddMinutes(10),
            notificationsEnabled: true,
            scheduled,
            isScheduledInWindows: true);

        Assert.AreEqual(
            NotificationPlatformAction.ReplaceScheduled,
            decision.Action);
        Assert.AreEqual(scheduled.Key, decision.Entry?.Key);
        Assert.AreNotEqual(
            scheduled.ContentFingerprint,
            decision.Entry?.ContentFingerprint);
    }

    [TestMethod]
    public void Evaluate_ImageOnlyChangeReplacesScheduleWithSameKey()
    {
        GameEntry original = CreateGame(baseStamina: 90);
        NotificationLedgerEntry scheduled = CreateLedger(
            original,
            leadMinutes: 15,
            NotificationState.Scheduled);
        GameEntry imageChanged = original with
        {
            ImageAssetId = "replacement.png",
        };

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            imageChanged,
            leadMinutes: 15,
            nowUtc: RecordedAtUtc.AddMinutes(10),
            notificationsEnabled: true,
            scheduled,
            isScheduledInWindows: true);

        Assert.AreEqual(
            NotificationPlatformAction.ReplaceScheduled,
            decision.Action);
        Assert.AreEqual(scheduled.Key, decision.Entry?.Key);
        Assert.AreNotEqual(
            scheduled.ContentFingerprint,
            decision.Entry?.ContentFingerprint);
    }

    [TestMethod]
    public void Evaluate_StaminaOrLeadChangeCreatesNewKey()
    {
        GameEntry original = CreateGame(baseStamina: 90);
        NotificationLedgerEntry scheduled = CreateLedger(
            original,
            leadMinutes: 15,
            NotificationState.Scheduled);
        GameEntry changed = original with
        {
            BaseStamina = 80,
            RecordedAtUtc = RecordedAtUtc.AddMinutes(1),
        };

        NotificationDecision staminaDecision =
            NotificationStateMachine.Evaluate(
                changed,
                leadMinutes: 15,
                nowUtc: RecordedAtUtc.AddMinutes(10),
                notificationsEnabled: true,
                scheduled,
                isScheduledInWindows: true);
        NotificationDecision leadDecision = NotificationStateMachine.Evaluate(
            original,
            leadMinutes: 20,
            nowUtc: RecordedAtUtc.AddMinutes(10),
            notificationsEnabled: true,
            scheduled,
            isScheduledInWindows: true);

        Assert.AreEqual(
            NotificationPlatformAction.ReplaceScheduled,
            staminaDecision.Action);
        Assert.AreNotEqual(scheduled.Key, staminaDecision.Entry?.Key);
        Assert.AreEqual(
            NotificationPlatformAction.ReplaceScheduled,
            leadDecision.Action);
        Assert.AreNotEqual(scheduled.Key, leadDecision.Entry?.Key);
    }

    [TestMethod]
    public void Evaluate_ConsumedCycleIsNeverRetried()
    {
        GameEntry game = CreateGame(baseStamina: 90);
        NotificationLedgerEntry consumed = CreateLedger(
            game,
            leadMinutes: 15,
            NotificationState.Consumed);

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            game,
            leadMinutes: 15,
            nowUtc: RecordedAtUtc.AddMinutes(10),
            notificationsEnabled: true,
            consumed,
            isScheduledInWindows: false);

        Assert.AreEqual(NotificationPlatformAction.None, decision.Action);
        Assert.AreSame(consumed, decision.Entry);
    }

    [TestMethod]
    public void Evaluate_LeadUnderflowPreservesExistingValidSchedule()
    {
        GameEntry validGame = CreateGame(baseStamina: 90);
        NotificationLedgerEntry scheduled = CreateLedger(
            validGame,
            leadMinutes: 15,
            NotificationState.Scheduled);
        GameEntry underflow = validGame with
        {
            BaseStamina = 99,
            RecoveryMinutes = 1,
            RecordedAtUtc = DateTimeOffset.MinValue,
        };

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            underflow,
            leadMinutes: 2,
            nowUtc: DateTimeOffset.MinValue,
            notificationsEnabled: true,
            scheduled,
            isScheduledInWindows: true);

        Assert.AreEqual(
            NotificationDecisionError.InvalidSchedule,
            decision.Error);
        Assert.AreEqual(NotificationPlatformAction.None, decision.Action);
        Assert.IsTrue(decision.IsExistingSchedulePreserved);
        Assert.AreSame(scheduled, decision.Entry);
    }

    [TestMethod]
    public void Evaluate_FullGameCancelsExistingSchedule()
    {
        GameEntry recovering = CreateGame(baseStamina: 90);
        NotificationLedgerEntry scheduled = CreateLedger(
            recovering,
            leadMinutes: 15,
            NotificationState.Scheduled);
        GameEntry full = recovering with { BaseStamina = 100 };

        NotificationDecision decision = NotificationStateMachine.Evaluate(
            full,
            leadMinutes: 15,
            RecordedAtUtc,
            notificationsEnabled: true,
            scheduled,
            isScheduledInWindows: true);

        Assert.AreEqual(NotificationPlatformAction.Cancel, decision.Action);
        Assert.IsNull(decision.Entry);
    }

    private static NotificationLedgerEntry CreateLedger(
        GameEntry game,
        int leadMinutes,
        NotificationState state)
    {
        StaminaSnapshot snapshot = StaminaCalculator.Calculate(
            game,
            RecordedAtUtc);
        Assert.IsNotNull(snapshot.FullAtUtc);
        return NotificationLedgerEntry.Create(
            game,
            snapshot.FullAtUtc.Value,
            leadMinutes,
            state);
    }

    private static GameEntry CreateGame(int baseStamina) => new(
        Guid.Parse("EAF52F97-591F-468B-A17B-153551A2053E"),
        "Test game",
        baseStamina,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        RecordedAtUtc,
        ImageAssetId: null,
        SortOrder: 0,
        RecoverySeconds: 0,
        IsNotificationEnabled: true);
}
