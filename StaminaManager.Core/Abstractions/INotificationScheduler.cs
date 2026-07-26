using StaminaManager.Core.Models;

namespace StaminaManager.Core.Abstractions;

public sealed record NotificationRequest(
    Guid GameId,
    string GameName,
    DateTimeOffset FullAtUtc,
    DateTimeOffset NotificationAtUtc);

public sealed class NotificationActivationEventArgs(Guid gameId)
    : EventArgs
{
    public Guid GameId { get; } = gameId;
}

public interface INotificationScheduler : IDisposable
{
    event EventHandler<NotificationActivationEventArgs>? ActivationRequested;

    void Initialize();

    Task<IReadOnlySet<Guid>> GetScheduledGameIdsAsync(
        CancellationToken cancellationToken);

    Task ScheduleAsync(
        NotificationRequest request,
        CancellationToken cancellationToken);

    Task ShowImmediateAsync(
        NotificationRequest request,
        CancellationToken cancellationToken);

    Task CancelAsync(
        Guid gameId,
        CancellationToken cancellationToken);

    Task CancelAllAsync(CancellationToken cancellationToken);
}
