using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;

namespace StaminaManager.Core.Abstractions;

public sealed record BackupPreview(
    int GameCount,
    int ImageCount,
    AppTheme Theme,
    BackdropKind Backdrop,
    bool NotificationsEnabled,
    CloseBehavior CloseBehavior,
    bool StartupEnabled,
    int AcrylicTintOpacityPercent,
    AppLanguage Language)
{
    public static BackupPreview From(
        AppSettings settings,
        int gameCount,
        int imageCount) => new(
            gameCount,
            imageCount,
            settings.Theme,
            settings.Backdrop,
            settings.NotificationsEnabled,
            settings.CloseBehavior,
            settings.StartupEnabled,
            settings.AcrylicTintOpacityPercent,
            settings.Language);
}

public sealed record BackupRestoreResult(
    BackupPreview Preview,
    DataEnvelope Data,
    string PreviousSnapshotPath,
    bool RequiresDerivedStateRetry,
    bool IsCommitted,
    bool IsPartial);

public sealed record PreparedBackupRestore(
    string SessionId,
    BackupPreview Preview);

public interface IBackupService
{
    Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken);

    Task<PreparedBackupRestore> PrepareRestoreAsync(
        string sourcePath,
        CancellationToken cancellationToken);

    Task CancelPreparedRestoreAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<BackupRestoreResult?> ResumeAsync(
        CancellationToken cancellationToken);

    Task AcknowledgeDerivedStateAsync(
        CancellationToken cancellationToken);
}
