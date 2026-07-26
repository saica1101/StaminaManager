using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Persistence;
using System.Text.Json;

namespace StaminaManager.Infrastructure.Backup;

internal sealed record RestoreJournal(
    RestoreJournalStage Stage,
    string SourcePath,
    bool RequiresDerivedStateRetry);

internal sealed class BackupTransactionStore
{
    private const string DataFileName = "data.json";
    private const string AssetsDirectoryName = "Assets";
    private const string StateDirectoryName = ".backup";
    private const string StageDirectoryName = "stage";
    private const string PreviousDirectoryName = "previous";
    private const string PendingDirectoryName = "previous.pending";
    private const string RollbackDirectoryName = "assets.rollback";
    private const string JournalFileName = "restore-journal.json";
    private readonly IAppDataPathProvider _pathProvider;

    public BackupTransactionStore(IAppDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        _pathProvider = pathProvider;
    }

    public string PreviousDirectory => GetStatePath(PreviousDirectoryName);

    public string CurrentAssetsDirectory => Path.Combine(
        _pathProvider.DataRootPath,
        AssetsDirectoryName);

    public string StageDataPath => Path.Combine(
        GetStatePath(StageDirectoryName),
        DataFileName);

    public async Task StageAsync(
        ValidatedBackup backup,
        CancellationToken cancellationToken)
    {
        string stage = GetStatePath(StageDirectoryName);
        ResetDirectory(stage);
        string assets = Path.Combine(stage, AssetsDirectoryName);
        Directory.CreateDirectory(assets);
        await WriteDataAsync(
            Path.Combine(stage, DataFileName),
            backup.Data,
            cancellationToken).ConfigureAwait(false);
        foreach (ValidatedBackupAsset asset in backup.Assets)
        {
            await File.WriteAllBytesAsync(
                Path.Combine(assets, Path.GetFileName(asset.EntryPath)),
                asset.Content,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        string pending = GetStatePath(PendingDirectoryName);
        ResetDirectory(pending);
        string currentData = GetCurrentDataPath();
        if (File.Exists(currentData))
        {
            File.Copy(currentData, Path.Combine(pending, DataFileName));
        }

        if (Directory.Exists(CurrentAssetsDirectory))
        {
            CopyDirectory(
                CurrentAssetsDirectory,
                Path.Combine(pending, AssetsDirectoryName));
        }

        if (Directory.Exists(PreviousDirectory))
        {
            Directory.Delete(PreviousDirectory, recursive: true);
        }

        Directory.Move(pending, PreviousDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        string rollback = GetStatePath(RollbackDirectoryName);
        if (Directory.Exists(rollback))
        {
            Directory.Delete(rollback, recursive: true);
        }

        if (Directory.Exists(CurrentAssetsDirectory))
        {
            Directory.Move(CurrentAssetsDirectory, rollback);
        }

        try
        {
            Directory.Move(
                Path.Combine(
                    GetStatePath(StageDirectoryName),
                    AssetsDirectoryName),
                CurrentAssetsDirectory);
            if (File.Exists(currentData))
            {
                File.Replace(
                    StageDataPath,
                    currentData,
                    destinationBackupFileName: null,
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(StageDataPath, currentData);
            }
        }
        catch
        {
            if (File.Exists(StageDataPath))
            {
                RollbackAssets(rollback);
            }

            throw;
        }

        await Task.CompletedTask;
    }

    public async Task RecoverAfterFailureAsync()
    {
        RestoreJournal? journal;
        try
        {
            journal = await ReadJournalAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch
        {
            return;
        }

        bool isCommitted = journal?.Stage
            >= RestoreJournalStage.LocalCommitted
            || journal?.Stage == RestoreJournalStage.Staged
                && !File.Exists(StageDataPath);
        if (!isCommitted)
        {
            RollbackAssets(GetStatePath(RollbackDirectoryName));
        }
    }

    public async Task RecoverPreCommitAsync(
        CancellationToken cancellationToken)
    {
        RestoreJournal? journal = await ReadJournalAsync(cancellationToken)
            .ConfigureAwait(false);
        if (journal?.Stage >= RestoreJournalStage.LocalCommitted)
        {
            throw new InvalidOperationException(
                "A committed restore still requires derived-state recovery.");
        }

        RollbackAssets(GetStatePath(RollbackDirectoryName));
        DeleteDirectoryIfExists(GetStatePath(StageDirectoryName));
        DeleteFileIfExists(GetJournalPath());
    }

    public async Task WriteJournalAsync(
        RestoreJournal journal,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(GetStateDirectory());
        string path = GetJournalPath();
        string temporaryPath = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(journal, SerializerOptions),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            DeleteFileIfExists(temporaryPath);
        }
    }

    public async Task<RestoreJournal?> ReadJournalAsync(
        CancellationToken cancellationToken)
    {
        string path = GetJournalPath();
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken)
                .ConfigureAwait(false);
            return JsonSerializer.Deserialize<RestoreJournal>(
                json,
                SerializerOptions)
                ?? throw new InvalidDataException(
                    "The restore journal is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The restore journal is invalid.",
                exception);
        }
    }

    public Task AcknowledgeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteDirectoryIfExists(GetStatePath(StageDirectoryName));
        DeleteDirectoryIfExists(GetStatePath(PendingDirectoryName));
        DeleteDirectoryIfExists(GetStatePath(RollbackDirectoryName));
        DeleteFileIfExists(GetJournalPath());
        return Task.CompletedTask;
    }

    private void RollbackAssets(string rollback)
    {
        if (!Directory.Exists(rollback))
        {
            return;
        }

        DeleteDirectoryIfExists(CurrentAssetsDirectory);
        Directory.Move(rollback, CurrentAssetsDirectory);
    }

    private static async Task WriteDataAsync(
        string path,
        DataEnvelope data,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            16 * 1024,
            FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(
            stream,
            data,
            JsonSerializationContext.Configured.DataEnvelope,
            cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
    }

    private static void ResetDirectory(string path)
    {
        DeleteDirectoryIfExists(path);
        Directory.CreateDirectory(path);
    }

    private static void DeleteDirectoryIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string GetStateDirectory() => Path.Combine(
        _pathProvider.DataRootPath,
        StateDirectoryName);

    private string GetStatePath(string name) => Path.Combine(
        GetStateDirectory(),
        name);

    private string GetJournalPath() => GetStatePath(JournalFileName);

    private string GetCurrentDataPath() => Path.Combine(
        _pathProvider.DataRootPath,
        DataFileName);

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
    };
}
