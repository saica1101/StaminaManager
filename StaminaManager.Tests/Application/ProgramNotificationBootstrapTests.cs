using StaminaManager.Core.Abstractions;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class ProgramNotificationBootstrapTests
{
    [TestMethod]
    public void InitializeNotificationsBeforeActivation_RegistersFirst()
    {
        List<string> operations = [];
        FakeNotificationScheduler scheduler = new(operations);

        string activation = Program.InitializeNotificationsBeforeActivation(
            scheduler,
            () =>
            {
                operations.Add("get-activation");
                return "activation";
            });

        Assert.AreEqual("activation", activation);
        CollectionAssert.AreEqual(
            new[] { "initialize", "get-activation" },
            operations);
    }

    [TestMethod]
    public void InitializeNotificationsBeforeActivation_DoesNotReadAfterFailure()
    {
        List<string> operations = [];
        FakeNotificationScheduler scheduler = new(operations)
        {
            InitializeFailure = new InvalidOperationException("register"),
        };

        Assert.ThrowsExactly<InvalidOperationException>(() =>
            Program.InitializeNotificationsBeforeActivation(
                scheduler,
                () =>
                {
                    operations.Add("get-activation");
                    return "activation";
                }));

        CollectionAssert.AreEqual(new[] { "initialize" }, operations);
    }

    private sealed class FakeNotificationScheduler(List<string> operations)
        : INotificationScheduler
    {
        public Exception? InitializeFailure { get; init; }

        public event EventHandler<NotificationActivationEventArgs>?
            ActivationRequested
        {
            add { }
            remove { }
        }

        public void Initialize()
        {
            operations.Add("initialize");
            if (InitializeFailure is not null)
            {
                throw InitializeFailure;
            }
        }

        public Task<IReadOnlySet<Guid>> GetScheduledGameIdsAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ScheduleAsync(
            NotificationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task ShowImmediateAsync(
            NotificationRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CancelAsync(
            Guid gameId,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task CancelAllAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
