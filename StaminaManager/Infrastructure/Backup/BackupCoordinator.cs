using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using StaminaManager.Infrastructure.Persistence;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;

namespace StaminaManager.Infrastructure.Backup;

public enum RestoreJournalStage
{
    Validated,
    Staged,
    LocalCommitted,
    Completed,
}

public sealed class BackupCoordinator : IBackupService
{
    private const string BackupExtension = ".staminabackup";
    private const string DataFileName = "data.json";
    private static readonly ConcurrentDictionary<string, SemaphoreSlim>
        OperationGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly SafeZipReader _reader;
    private readonly ILocalDataStore _dataStore;
    private readonly Action<RestoreJournalStage>? _failureInjector;
    private readonly BackupTransactionStore _transactions;
    private readonly SemaphoreSlim _operationGate;

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
        string root = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(pathProvider.DataRootPath));
        _operationGate = OperationGates.GetOrAdd(
            root,
            static _ => new SemaphoreSlim(1, 1));
    }

    public async Task ExportAsync(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ValidateBackupPath(destinationPath, mustExist: false);
        await _operationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        string fullDestination = Path.GetFullPath(destinationPath);
        string temporaryPath = fullDestination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            DataLoadResult loaded = await _dataStore.LoadAsync(
                cancellationToken).ConfigureAwait(false);
            DataEnvelope data = loaded.Envelope
                ?? throw new InvalidOperationException(
                    "バックアップ対象のデータがありません。");
            IReadOnlyList<ExportAsset> assets = ResolveExportAssets(data);
            Directory.CreateDirectory(
                Path.GetDirectoryName(fullDestination)
                ?? throw new InvalidOperationException(
                    "The destination directory is invalid."));
            await WriteArchiveAsync(
                temporaryPath,
                data,
                assets,
                cancellationToken).ConfigureAwait(false);

            await using (FileStream verification = OpenRead(temporaryPath))
            {
                _ = await _reader.ReadAsync(
                    verification,
                    cancellationToken).ConfigureAwait(false);
            }

            FileInfo backupInfo = new(temporaryPath);
            if (backupInfo.Length > BackupLimits.MaxBackupBytes)
            {
                throw new InvalidDataException(
                    "The backup file is too large.");
            }

            File.Move(temporaryPath, fullDestination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            _operationGate.Release();
        }
    }

    public async Task<BackupPreview> PreviewAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ValidateBackupPath(sourcePath, mustExist: true);
        await using FileStream stream = OpenRead(sourcePath);
        ValidatedBackup backup = await _reader.ReadAsync(
            stream,
            cancellationToken).ConfigureAwait(false);
        return backup.Preview;
    }

    public async Task<BackupRestoreResult> RestoreAsync(
        string sourcePath,
        CancellationToken cancellationToken)
    {
        ValidateBackupPath(sourcePath, mustExist: true);
        await _operationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await _transactions.RecoverPreCommitAsync(cancellationToken)
                .ConfigureAwait(false);
            ValidatedBackup backup;
            await using (FileStream stream = OpenRead(sourcePath))
            {
                backup = await _reader.ReadAsync(
                    stream,
                    cancellationToken).ConfigureAwait(false);
            }

            await _transactions.WriteJournalAsync(
                new RestoreJournal(
                    RestoreJournalStage.Validated,
                    Path.GetFullPath(sourcePath),
                    RequiresDerivedStateRetry: false),
                cancellationToken).ConfigureAwait(false);
            InjectFailure(RestoreJournalStage.Validated);

            await _transactions.StageAsync(backup, cancellationToken)
                .ConfigureAwait(false);
            await _transactions.WriteJournalAsync(
                new RestoreJournal(
                    RestoreJournalStage.Staged,
                    Path.GetFullPath(sourcePath),
                    RequiresDerivedStateRetry: false),
                cancellationToken).ConfigureAwait(false);
            InjectFailure(RestoreJournalStage.Staged);

            await _transactions.CommitAsync(cancellationToken)
                .ConfigureAwait(false);
            await _transactions.WriteJournalAsync(
                new RestoreJournal(
                    RestoreJournalStage.LocalCommitted,
                    Path.GetFullPath(sourcePath),
                    RequiresDerivedStateRetry: true),
                cancellationToken).ConfigureAwait(false);
            InjectFailure(RestoreJournalStage.LocalCommitted);

            return CreateResult(backup, requiresDerivedStateRetry: true);
        }
        catch
        {
            await _transactions.RecoverAfterFailureAsync()
                .ConfigureAwait(false);
            throw;
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
                return null;
            }

            bool isCommitted = journal.Stage
                >= RestoreJournalStage.LocalCommitted
                || !File.Exists(_transactions.StageDataPath)
                    && journal.Stage == RestoreJournalStage.Staged;
            if (!isCommitted)
            {
                await _transactions.RecoverPreCommitAsync(cancellationToken)
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
                RequiresDerivedStateRetry: true);
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

    private async Task WriteArchiveAsync(
        string path,
        DataEnvelope data,
        IReadOnlyList<ExportAsset> assets,
        CancellationToken cancellationToken)
    {
        await using FileStream output = new(
            path,
            FileMode.CreateNew,
            FileAccess.ReadWrite,
            FileShare.None,
            80 * 1024,
            FileOptions.Asynchronous);
        using ZipArchive archive = new(
            output,
            ZipArchiveMode.Create,
            leaveOpen: true);
        BackupManifest manifest = new(
            BackupManifest.CurrentSchemaVersion,
            DataEnvelope.CurrentSchemaVersion,
            DataFileName,
            assets.Select(static asset => new BackupAssetManifest(
                asset.AssetId,
                asset.EntryPath,
                asset.MediaType)).ToArray());
        await WriteJsonEntryAsync(
            archive,
            "manifest.json",
            manifest,
            cancellationToken).ConfigureAwait(false);
        ZipArchiveEntry dataEntry = archive.CreateEntry(
            DataFileName,
            CompressionLevel.NoCompression);
        await using (Stream dataStream = dataEntry.Open())
        {
            await JsonSerializer.SerializeAsync(
                dataStream,
                data,
                JsonSerializationContext.Configured.DataEnvelope,
                cancellationToken).ConfigureAwait(false);
        }

        foreach (ExportAsset asset in assets)
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                asset.EntryPath,
                CompressionLevel.NoCompression);
            await using Stream destination = entry.Open();
            await using FileStream source = OpenRead(asset.SourcePath);
            await source.CopyToAsync(destination, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(
            entryName,
            CompressionLevel.NoCompression);
        await using Stream stream = entry.Open();
        await JsonSerializer.SerializeAsync(
            stream,
            value,
            SerializerOptions,
            cancellationToken).ConfigureAwait(false);
    }

    private IReadOnlyList<ExportAsset> ResolveExportAssets(
        DataEnvelope data)
    {
        string assetsDirectory = _transactions.CurrentAssetsDirectory;
        List<ExportAsset> assets = [];
        foreach (string assetId in data.Games
            .Select(static game => game.ImageAssetId)
            .Where(static assetId => assetId is not null)
            .Select(static assetId => assetId!)
            .Distinct(StringComparer.Ordinal))
        {
            string png = Path.Combine(assetsDirectory, assetId + ".png");
            string jpeg = Path.Combine(assetsDirectory, assetId + ".jpg");
            string sourcePath;
            string extension;
            string mediaType;
            if (File.Exists(png) && !File.Exists(jpeg))
            {
                sourcePath = png;
                extension = ".png";
                mediaType = "image/png";
            }
            else if (File.Exists(jpeg) && !File.Exists(png))
            {
                sourcePath = jpeg;
                extension = ".jpg";
                mediaType = "image/jpeg";
            }
            else
            {
                throw new InvalidDataException(
                    "A referenced image is missing or ambiguous.");
            }

            assets.Add(new ExportAsset(
                assetId,
                $"assets/{assetId}{extension}",
                mediaType,
                sourcePath));
        }

        return assets;
    }

    private BackupRestoreResult CreateResult(
        ValidatedBackup backup,
        bool requiresDerivedStateRetry) => new(
            backup.Preview,
            backup.Data,
            _transactions.PreviousDirectory,
            requiresDerivedStateRetry);

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

    private sealed record ExportAsset(
        string AssetId,
        string EntryPath,
        string MediaType,
        string SourcePath);

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
    };
}
