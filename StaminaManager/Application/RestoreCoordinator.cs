using StaminaManager.Core.Abstractions;

namespace StaminaManager.Application;

public sealed class RestoreCoordinator
{
    private readonly IBackupService _backupService;
    private readonly IPreparedBackupCommitter _backupCommitter;
    private readonly GameManager _gameManager;

    public RestoreCoordinator(
        IBackupService backupService,
        GameManager gameManager)
    {
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(gameManager);
        _backupService = backupService;
        _backupCommitter = backupService as IPreparedBackupCommitter
            ?? throw new ArgumentException(
                "The backup service cannot commit prepared restores.",
                nameof(backupService));
        _gameManager = gameManager;
    }

    public Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken) =>
        _backupService.ExportAsync(destinationPath, cancellationToken);

    public Task<PreparedBackupRestore> PrepareAsync(
        string sourcePath,
        CancellationToken cancellationToken) =>
        _backupService.PrepareRestoreAsync(sourcePath, cancellationToken);

    public async Task<BackupRestoreResult> CommitPreparedAsync(
        string sessionId,
        bool isReplacementConfirmed,
        CancellationToken cancellationToken)
    {
        if (!isReplacementConfirmed)
        {
            throw new InvalidOperationException(
                "復元には現在データを置き換える明示確認が必要です。");
        }

        return await _gameManager.CommitRestoreAsync(
                (publish, token) =>
                    _backupCommitter.CommitPreparedRestoreAsync(
                        sessionId,
                        publish,
                        token),
                cancellationToken)
            .ConfigureAwait(false);
    }

    public Task CancelPreparedAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        _backupService.CancelPreparedRestoreAsync(
            sessionId,
            cancellationToken);

    public async Task<BackupRestoreResult?> ResumeAsync(
        CancellationToken cancellationToken)
    {
        BackupRestoreResult? result = await _backupService.ResumeAsync(
            cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            await _gameManager.ReplaceFromRestoreAsync(
                result.Data,
                cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public Task AcknowledgeDerivedStateAsync(
        CancellationToken cancellationToken) =>
        _backupService.AcknowledgeDerivedStateAsync(cancellationToken);
}
