using StaminaManager.Application;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Backup;
using StaminaManager.Infrastructure.Persistence;
using StaminaManager.Tests.TestDoubles;
using System.Collections.Immutable;

namespace StaminaManager.Tests.Backup;

internal sealed class RestoreWorkflowTestStore : IAsyncDisposable
{
    private RestoreWorkflowTestStore(
        string root,
        RestoreWorkflowPathProvider paths,
        LocalDataStore store)
    {
        Root = root;
        Paths = paths;
        Store = store;
    }

    public string Root { get; }

    public RestoreWorkflowPathProvider Paths { get; }

    public LocalDataStore Store { get; }

    public static async Task<RestoreWorkflowTestStore> CreateAsync(
        string gameName,
        bool startupEnabled)
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "StaminaManager.RestoreWorkflowTests",
            Guid.NewGuid().ToString("N"));
        RestoreWorkflowPathProvider paths = new(Path.Combine(root, "Data"));
        LocalDataStore store = new(paths);
        Directory.CreateDirectory(paths.DataRootPath);
        await store.SaveAsync(
            CreateEnvelope(gameName, startupEnabled),
            CancellationToken.None);
        return new RestoreWorkflowTestStore(root, paths, store);
    }

    public GameManager CreateManager() => new(
        Store,
        new FakeClock(DateTimeOffset.UtcNow),
        AppSettings.CreateDefault(AppTheme.Light));

    public BackupCoordinator CreateBackup(
        Action<RestoreJournalStage>? injector = null,
        Action<RestoreJournalStage>? journalWriteInjector = null) => new(
            new SafeZipReader(),
            Store,
            Paths,
            injector,
            journalWriteInjector);

    public async Task<string> ExportAsync()
    {
        string path = Path.Combine(Root, "data.staminabackup");
        await CreateBackup().ExportAsync(path, CancellationToken.None);
        return path;
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }

        return ValueTask.CompletedTask;
    }

    private static DataEnvelope CreateEnvelope(
        string gameName,
        bool startupEnabled)
    {
        GameEntry game = new(
            Guid.NewGuid(),
            gameName,
            BaseStamina: 1,
            MaxStamina: 100,
            RecoveryMinutes: 5,
            DateTimeOffset.UtcNow,
            ImageAssetId: null,
            SortOrder: 0);
        return new DataEnvelope(
            DataEnvelope.CurrentSchemaVersion,
            ImmutableArray.Create(game),
            AppSettings.CreateDefault(AppTheme.Light) with
            {
                StartupEnabled = startupEnabled,
                SelectedCompactGameId = game.Id,
            });
    }
}

internal sealed class RestoreWorkflowPathProvider(string dataRootPath)
    : IAppDataPathProvider
{
    public string DataRootPath { get; } = dataRootPath;
}
