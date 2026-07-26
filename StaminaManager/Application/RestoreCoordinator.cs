using StaminaManager.Core.Abstractions;

namespace StaminaManager.Application;

public sealed class RestoreCoordinator
{
    private readonly IBackupService _backupService;
    private readonly GameManager _gameManager;

    public RestoreCoordinator(
        IBackupService backupService,
        GameManager gameManager)
    {
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(gameManager);
        _backupService = backupService;
        _gameManager = gameManager;
    }

    public Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken) =>
        _backupService.ExportAsync(destinationPath, cancellationToken);

    public Task<BackupPreview> PreviewAsync(
        string sourcePath,
        CancellationToken cancellationToken) =>
        _backupService.PreviewAsync(sourcePath, cancellationToken);

    public async Task<BackupRestoreResult> RestoreAsync(
        string sourcePath,
        bool isReplacementConfirmed,
        CancellationToken cancellationToken)
    {
        if (!isReplacementConfirmed)
        {
            throw new InvalidOperationException(
                "復元には現在データを置き換える明示確認が必要です。");
        }

        BackupRestoreResult result = await _backupService.RestoreAsync(
            sourcePath,
            cancellationToken).ConfigureAwait(false);
        await _gameManager.ReplaceFromRestoreAsync(
            result.Data,
            cancellationToken).ConfigureAwait(false);
        return result;
    }

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
