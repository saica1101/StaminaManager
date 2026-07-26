using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Persistence;

namespace StaminaManager.Application;

internal interface IPreparedBackupCommitter
{
    Task<BackupRestoreResult> CommitPreparedRestoreAsync(
        string sessionId,
        Func<DataEnvelope, CancellationToken, Task>
            publishCommittedDataAsync,
        CancellationToken cancellationToken);
}
