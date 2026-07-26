using StaminaManager.Core.Abstractions;
using StaminaManager.Infrastructure.Notifications;

namespace StaminaManager.Tests.Notifications;

[TestClass]
public sealed class WindowsNotificationSchedulerTests
{
    [TestMethod]
    public void Initialize_RegistersHandlerBeforePlatformRegistrationOnce()
    {
        FakeWindowsNotificationPlatformAdapter adapter = new();
        using WindowsNotificationScheduler scheduler = new(adapter);

        scheduler.Initialize();
        scheduler.Initialize();

        CollectionAssert.AreEqual(
            new[] { "handler-added", "register" },
            adapter.Operations);
    }

    [TestMethod]
    public async Task ScheduleAsync_CancelsMatchingGameBeforeAdd()
    {
        Guid gameId = Guid.NewGuid();
        FakeWindowsNotificationPlatformAdapter adapter = new(
            [
                new WindowsScheduledNotification(
                    gameId.ToString("N"),
                    WindowsNotificationScheduler.NotificationGroup),
            ]);
        using WindowsNotificationScheduler scheduler = new(adapter);
        NotificationRequest request = CreateRequest(gameId);

        await scheduler.ScheduleAsync(request, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { $"remove:{gameId:N}", $"add:{gameId:N}" },
            adapter.Operations);
    }

    [TestMethod]
    public async Task GetScheduledGameIdsAsync_IgnoresOtherGroupsAndBadTags()
    {
        Guid expected = Guid.NewGuid();
        FakeWindowsNotificationPlatformAdapter adapter = new(
            [
                new WindowsScheduledNotification(
                    expected.ToString("N"),
                    WindowsNotificationScheduler.NotificationGroup),
                new WindowsScheduledNotification(
                    Guid.NewGuid().ToString("N"),
                    "other"),
                new WindowsScheduledNotification(
                    "not-a-guid",
                    WindowsNotificationScheduler.NotificationGroup),
            ]);
        using WindowsNotificationScheduler scheduler = new(adapter);

        IReadOnlySet<Guid> scheduled =
            await scheduler.GetScheduledGameIdsAsync(
                CancellationToken.None);

        Assert.HasCount(1, scheduled);
        Assert.Contains(expected, scheduled);
    }

    [TestMethod]
    public async Task ShowImmediateAsync_RejectsUnacceptedNotification()
    {
        FakeWindowsNotificationPlatformAdapter adapter = new()
        {
            ShowAccepted = false,
        };
        using WindowsNotificationScheduler scheduler = new(adapter);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => scheduler.ShowImmediateAsync(
                CreateRequest(Guid.NewGuid()),
                CancellationToken.None));
    }

    [TestMethod]
    public void Activation_OnlyRoutesSingleValidGameIdArgument()
    {
        Guid gameId = Guid.NewGuid();
        FakeWindowsNotificationPlatformAdapter adapter = new();
        using WindowsNotificationScheduler scheduler = new(adapter);
        List<Guid> activated = [];
        scheduler.ActivationRequested += (_, args) =>
            activated.Add(args.GameId);
        scheduler.Initialize();

        adapter.RaiseActivation($"gameId={gameId:N}");
        adapter.RaiseActivation($"other=1&gameId={gameId:N}");
        adapter.RaiseActivation("gameId=not-a-guid");
        adapter.RaiseActivation($"gameId={gameId:N}&gameId={gameId:N}");

        CollectionAssert.AreEqual(new[] { gameId }, activated);
    }

    [TestMethod]
    public void Activation_BeforeSubscriberIsDeliveredAfterSubscription()
    {
        Guid gameId = Guid.NewGuid();
        FakeWindowsNotificationPlatformAdapter adapter = new();
        using WindowsNotificationScheduler scheduler = new(adapter);
        scheduler.Initialize();

        adapter.RaiseActivation($"gameId={gameId:N}");

        List<Guid> activated = [];
        scheduler.ActivationRequested += (_, args) =>
            activated.Add(args.GameId);

        CollectionAssert.AreEqual(new[] { gameId }, activated);
    }

    [TestMethod]
    public void Dispose_UnregistersWithoutDeletingScheduledNotifications()
    {
        FakeWindowsNotificationPlatformAdapter adapter = new();
        WindowsNotificationScheduler scheduler = new(adapter);
        scheduler.Initialize();

        scheduler.Dispose();
        scheduler.Dispose();

        CollectionAssert.AreEqual(
            new[]
            {
                "handler-added",
                "register",
                "handler-removed",
                "unregister",
            },
            adapter.Operations);
    }

    [TestMethod]
    public void Dispose_UnregisterFailureDoesNotBlockApplicationExit()
    {
        FakeWindowsNotificationPlatformAdapter adapter = new()
        {
            UnregisterFailure = new InvalidOperationException("COM failure"),
        };
        WindowsNotificationScheduler scheduler = new(adapter);
        scheduler.Initialize();

        scheduler.Dispose();
        scheduler.Dispose();

        Assert.AreEqual(
            1,
            adapter.Operations.Count(operation => operation == "unregister"));
    }

    private static NotificationRequest CreateRequest(Guid gameId) => new(
        gameId,
        "Test game",
        FullAtUtc: new DateTimeOffset(
            2026,
            7,
            25,
            1,
            0,
            0,
            TimeSpan.Zero),
        NotificationAtUtc: new DateTimeOffset(
            2026,
            7,
            25,
            0,
            45,
            0,
            TimeSpan.Zero));

    private sealed class FakeWindowsNotificationPlatformAdapter
        : IWindowsNotificationPlatformAdapter
    {
        private readonly List<WindowsScheduledNotification> _scheduled;
        private EventHandler<string>? _activationReceived;

        public FakeWindowsNotificationPlatformAdapter(
            IEnumerable<WindowsScheduledNotification>? scheduled = null)
        {
            _scheduled = scheduled?.ToList() ?? [];
        }

        public event EventHandler<string>? ActivationReceived
        {
            add
            {
                Operations.Add("handler-added");
                _activationReceived += value;
            }
            remove
            {
                Operations.Add("handler-removed");
                _activationReceived -= value;
            }
        }

        public List<string> Operations { get; } = [];

        public bool ShowAccepted { get; init; } = true;

        public Exception? UnregisterFailure { get; init; }

        public void Register() => Operations.Add("register");

        public void Unregister()
        {
            Operations.Add("unregister");
            if (UnregisterFailure is not null)
            {
                throw UnregisterFailure;
            }
        }

        public IReadOnlyList<WindowsScheduledNotification> GetScheduled() =>
            _scheduled;

        public void AddToSchedule(NotificationRequest request)
        {
            Operations.Add($"add:{request.GameId:N}");
            _scheduled.Add(new WindowsScheduledNotification(
                request.GameId.ToString("N"),
                WindowsNotificationScheduler.NotificationGroup));
        }

        public bool Show(NotificationRequest request)
        {
            Operations.Add($"show:{request.GameId:N}");
            return ShowAccepted;
        }

        public void Remove(WindowsScheduledNotification notification)
        {
            Operations.Add($"remove:{notification.Tag}");
            _scheduled.Remove(notification);
        }

        public void RaiseActivation(string argument) =>
            _activationReceived?.Invoke(this, argument);
    }
}
