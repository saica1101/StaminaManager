using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;

namespace StaminaManager.Core.Abstractions;

public sealed record BackupPreview(
    int GameCount,
    int ImageCount,
    string Theme,
    string Backdrop,
    bool NotificationsEnabled,
    string CloseBehavior,
    bool StartupEnabled)
{
    public static BackupPreview From(
        AppSettings settings,
        int gameCount,
        int imageCount) => new(
            gameCount,
            imageCount,
            settings.Theme.ToString(),
            settings.Backdrop.ToString(),
            settings.NotificationsEnabled,
            settings.CloseBehavior.ToString(),
            settings.StartupEnabled);
}

public sealed record BackupRestoreResult(
    BackupPreview Preview,
    DataEnvelope Data,
    string PreviousSnapshotPath,
    bool RequiresDerivedStateRetry);

public interface IBackupService
{
    Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken);

    Task<BackupPreview> PreviewAsync(
        string sourcePath,
        CancellationToken cancellationToken);

    Task<BackupRestoreResult> RestoreAsync(
        string sourcePath,
        CancellationToken cancellationToken);

    Task<BackupRestoreResult> RestoreAndPublishAsync(
        string sourcePath,
        Func<DataEnvelope, CancellationToken, Task>
            publishCommittedDataAsync,
        CancellationToken cancellationToken);

    Task<BackupRestoreResult?> ResumeAsync(
        CancellationToken cancellationToken);

    Task AcknowledgeDerivedStateAsync(
        CancellationToken cancellationToken);
}
