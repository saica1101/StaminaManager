using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using StaminaManager.Application;
using StaminaManager.Infrastructure.Persistence;
using System.Collections.Concurrent;

namespace StaminaManager.Infrastructure.Backup;

public enum RestoreJournalStage
{
    Validated,
    Staged,
    LocalCommitted,
    Completed,
}

public sealed partial class BackupCoordinator
    : IBackupService,
      IPreparedBackupCommitter
{
    private const string BackupExtension = ".staminabackup";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        OperationGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly SafeZipReader _reader;
    private readonly ILocalDataStore _dataStore;
    private readonly Action<RestoreJournalStage>? _failureInjector;
    private readonly BackupTransactionStore _transactions;
    private readonly SemaphoreSlim _operationGate;
    private readonly SemaphoreSlim _dataWriteGate;
    private readonly SemaphoreSlim _assetGate;

    public BackupCoordinator(
        SafeZipReader reader,
        ILocalDataStore dataStore,
        IAppDataPathProvider pathProvider,
        Action<RestoreJournalStage>? failureInjector = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(dataStore);
        ArgumentNullException.ThrowIfNull(pathProvider);
        _reader = reader;
        _dataStore = dataStore;
        _failureInjector = failureInjector;
        _transactions = new BackupTransactionStore(pathProvider);
        _dataWriteGate = AppDataWriteGate.Get(pathProvider);
        _assetGate = AppAssetGate.Get(pathProvider);
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(pathProvider.DataRootPath));
        _operationGate = OperationGates.GetOrAdd(
            root,
            static _ => new SemaphoreSlim(1, 1));
    }

    public async Task<PreparedBackupRestore> PrepareRestoreAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ValidateBackupPath(sourcePath, mustExist: true);
        await _operationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await RecoverPreCommitWithGatesAsync(cancellationToken)
                .ConfigureAwait(false);
            string sessionId = Guid.NewGuid().ToString("N");
            _transactions.BeginStage();
            ValidatedBackup backup;
            await using (FileStream stream = OpenRead(sourcePath))
            {
                backup = await _reader.ReadToStageAsync(
                    stream,
                    _transactions.StageAssetsDirectory,
                    cancellationToken).ConfigureAwait(false);
            }

            await _transactions.WriteJournalAsync(
                new RestoreJournal(
                    RestoreJournalStage.Validated,
                    sessionId,
                    RequiresDerivedStateRetry: false),
                cancellationToken).ConfigureAwait(false);
            InjectFailure(RestoreJournalStage.Validated);
            await _transactions.WriteStagedDataAsync(
                backup.Data,
                cancellationToken).ConfigureAwait(false);
            await _transactions.WriteJournalAsync(
                new RestoreJournal(
                    RestoreJournalStage.Staged,
                    sessionId,
                    RequiresDerivedStateRetry: false),
                cancellationToken).ConfigureAwait(false);
            InjectFailure(RestoreJournalStage.Staged);
            return new PreparedBackupRestore(sessionId, backup.Preview);
        }
        catch
        {
            await RecoverAfterFailureWithGatesAsync()
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    async Task<BackupRestoreResult>
        IPreparedBackupCommitter.CommitPreparedRestoreAsync(
        string sessionId,
        Func<DataEnvelope, CancellationToken, Task>
            publishCommittedDataAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(publishCommittedDataAsync);
        await _operationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await _transactions.EnsurePreparedSessionAsync(
                sessionId,
                cancellationToken).ConfigureAwait(false);
            DataEnvelope data = await _transactions.ReadStagedDataAsync(
                cancellationToken).ConfigureAwait(false);
            BackupArchiveValidator.ValidateData(data);
            await _dataWriteGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await _assetGate.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    await _transactions.CommitAsync(cancellationToken)
                        .ConfigureAwait(false);
                    await _transactions.WriteJournalAsync(
                        new RestoreJournal(
                            RestoreJournalStage.LocalCommitted,
                            sessionId,
                            RequiresDerivedStateRetry: true),
                        cancellationToken).ConfigureAwait(false);
                    InjectFailure(RestoreJournalStage.LocalCommitted);
                    await publishCommittedDataAsync(
                        data,
                        cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _assetGate.Release();
                }
            }
            finally
            {
                _dataWriteGate.Release();
            }

            return CreateResult(data, requiresDerivedStateRetry: true);
        }
        catch
        {
            await RecoverAfterFailureWithGatesAsync()
                .ConfigureAwait(false);
            throw;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task CancelPreparedRestoreAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        await _operationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await _transactions.EnsurePreparedSessionAsync(
                sessionId,
                cancellationToken).ConfigureAwait(false);
            await RecoverPreCommitWithGatesAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<BackupRestoreResult?> ResumeAsync(
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            RestoreJournal? journal = await _transactions.ReadJournalAsync(
                cancellationToken).ConfigureAwait(false);
            if (journal is null)
            {
                await RecoverPreCommitWithGatesAsync(cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }

            if (journal.Stage == RestoreJournalStage.Completed
                && !journal.RequiresDerivedStateRetry)
            {
                await _transactions.AcknowledgeAsync(cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }

            bool isCommitted = journal.Stage
                >= RestoreJournalStage.LocalCommitted
                || !File.Exists(_transactions.StageDataPath)
                    && journal.Stage == RestoreJournalStage.Staged;
            if (!isCommitted)
            {
                await RecoverPreCommitWithGatesAsync(cancellationToken)
                    .ConfigureAwait(false);
                return null;
            }

            DataLoadResult loaded = await _dataStore.LoadAsync(
                cancellationToken).ConfigureAwait(false);
            DataEnvelope data = loaded.Envelope
                ?? throw new InvalidDataException(
                    "Committed restore data is not readable.");
            BackupPreview preview = BackupPreview.From(
                data.Settings,
                data.Games.Length,
                CountReferencedAssets(data));
            return new BackupRestoreResult(
                preview,
                data,
                _transactions.PreviousDirectory,
                RequiresDerivedStateRetry: true,
                IsCommitted: true,
                IsPartial: true);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task AcknowledgeDerivedStateAsync(
        CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            RestoreJournal? journal = await _transactions.ReadJournalAsync(
                cancellationToken).ConfigureAwait(false);
            if (journal is not null)
            {
                await _transactions.WriteJournalAsync(
                    journal with
                    {
                        Stage = RestoreJournalStage.Completed,
                        RequiresDerivedStateRetry = false,
                    },
                    cancellationToken).ConfigureAwait(false);
                InjectFailure(RestoreJournalStage.Completed);
            }

            await _transactions.AcknowledgeAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private BackupRestoreResult CreateResult(
        DataEnvelope data,
        bool requiresDerivedStateRetry) => new(
            BackupPreview.From(
                data.Settings,
                data.Games.Length,
                CountReferencedAssets(data)),
            data,
            _transactions.PreviousDirectory,
            requiresDerivedStateRetry,
            IsCommitted: true,
            IsPartial: requiresDerivedStateRetry);

    private async Task RecoverPreCommitWithGatesAsync(
        CancellationToken cancellationToken)
    {
        await _dataWriteGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await _assetGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await _transactions.RecoverPreCommitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                _assetGate.Release();
            }
        }
        finally
        {
            _dataWriteGate.Release();
        }
    }

    private async Task RecoverAfterFailureWithGatesAsync()
    {
        await _dataWriteGate.WaitAsync(CancellationToken.None)
            .ConfigureAwait(false);
        try
        {
            await _assetGate.WaitAsync(CancellationToken.None)
                .ConfigureAwait(false);
            try
            {
                await _transactions.RecoverAfterFailureAsync()
                    .ConfigureAwait(false);
            }
            finally
            {
                _assetGate.Release();
            }
        }
        finally
        {
            _dataWriteGate.Release();
        }
    }

    private static void ValidateBackupPath(string path, bool mustExist)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!string.Equals(
            Path.GetExtension(path),
            BackupExtension,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "Backup files must use the .staminabackup extension.");
        }

        if (mustExist)
        {
            FileInfo info = new(path);
            if (!info.Exists)
            {
                throw new FileNotFoundException(
                    "The selected backup file does not exist.",
                    path);
            }

            if (info.Length > BackupLimits.MaxBackupBytes)
            {
                throw new InvalidDataException(
                    "The backup file is too large.");
            }
        }
    }

    private static FileStream OpenRead(string path) => new(
        path,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        80 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static int CountReferencedAssets(DataEnvelope data) => data.Games
        .Select(static game => game.ImageAssetId)
        .Where(static assetId => assetId is not null)
        .Distinct(StringComparer.Ordinal)
        .Count();

    private void InjectFailure(RestoreJournalStage stage) =>
        _failureInjector?.Invoke(stage);

}
