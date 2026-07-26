using StaminaManager.Core.Models;

namespace StaminaManager.Core.Abstractions;

public interface INotificationLedgerStore
{
    Task<IReadOnlyList<NotificationLedgerEntry>> LoadAsync(
        CancellationToken cancellationToken);

    Task SaveAsync(
        IReadOnlyCollection<NotificationLedgerEntry> entries,
        CancellationToken cancellationToken);
}
