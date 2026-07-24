using StaminaManager.Core.Persistence;

namespace StaminaManager.Core.Abstractions;

public interface ILocalDataStore
{
    Task<DataLoadResult> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(
        DataEnvelope envelope,
        CancellationToken cancellationToken);
}
