using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Persistence;
using System.Text.Json;

namespace StaminaManager.Infrastructure.Backup;

internal sealed record RestoreJournal(
    RestoreJournalStage Stage,
    string SessionId,
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
    private readonly Action<RestoreJournalStage>? _journalWriteInjector;

    public BackupTransactionStore(
        IAppDataPathProvider pathProvider,
        Action<RestoreJournalStage>? journalWriteInjector = null)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        _pathProvider = pathProvider;
        _journalWriteInjector = journalWriteInjector;
    }

    public string PreviousDirectory => GetStatePath(PreviousDirectoryName);

    public string CurrentAssetsDirectory => Path.Combine(
        _pathProvider.DataRootPath,
        AssetsDirectoryName);

    public string StageDataPath => Path.Combine(
        GetStatePath(StageDirectoryName),
        DataFileName);

    public string StageDirectory => GetStatePath(StageDirectoryName);

    public string StageAssetsDirectory => Path.Combine(
        StageDirectory,
        AssetsDirectoryName);

    public bool HasStagedData => File.Exists(StageDataPath);

    public bool HasCommitEvidence => Directory.Exists(
            GetStatePath(RollbackDirectoryName))
        || Directory.Exists(StageDirectory)
            && !Directory.Exists(StageAssetsDirectory)
            && !HasStagedData;

    public void BeginStage()
    {
        ResetDirectory(StageDirectory);
        Directory.CreateDirectory(StageAssetsDirectory);
    }

    public Task WriteStagedDataAsync(
        DataEnvelope data,
        CancellationToken cancellationToken) => WriteDataAsync(
            StageDataPath,
            data,
            cancellationToken);

    public async Task<DataEnvelope> ReadStagedDataAsync(
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            StageDataPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync(
                stream,
                JsonSerializationContext.Configured.DataEnvelope,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException(
                "The staged restore data is empty.");
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
            DeleteDirectoryIfExists(GetStatePath(StageDirectoryName));
            DeleteDirectoryIfExists(GetStatePath(PendingDirectoryName));
            DeleteFileIfExists(GetJournalPath());
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
        DeleteDirectoryIfExists(GetStatePath(PendingDirectoryName));
        DeleteFileIfExists(GetJournalPath());
    }

    public async Task EnsurePreparedSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        RestoreJournal? journal = await ReadJournalAsync(cancellationToken)
            .ConfigureAwait(false);
        if (journal?.Stage != RestoreJournalStage.Staged
            || !string.Equals(
                journal.SessionId,
                sessionId,
                StringComparison.Ordinal)
            || !File.Exists(StageDataPath)
            || !Directory.Exists(StageAssetsDirectory))
        {
            throw new InvalidOperationException(
                "The prepared restore session is unavailable.");
        }
    }

    public async Task WriteJournalAsync(
        RestoreJournal journal,
        CancellationToken cancellationToken)
    {
        _journalWriteInjector?.Invoke(journal.Stage);
        Directory.CreateDirectory(GetStateDirectory());
        string path = GetJournalPath();
        string temporaryPath = path + ".tmp";
        try
        {
            await using (FileStream stream = new(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    journal,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

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

    public void QuarantineCorruptJournal()
    {
        string journal = GetJournalPath();
        if (!File.Exists(journal))
        {
            return;
        }

        string quarantine = Path.Combine(
            GetStateDirectory(),
            $"restore-journal.corrupt-{Guid.NewGuid():N}.json");
        File.Move(journal, quarantine);
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
