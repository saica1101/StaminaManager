using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Backup;
using StaminaManager.Infrastructure.Persistence;
using StaminaManager.Infrastructure.Storage;

namespace StaminaManager.Tests.Backup;

[TestClass]
public sealed class PreparedRestoreTests
{
    [TestMethod]
    public void IBackupService_pathから直接commitするunsafeApiを公開しない()
    {
        string[] methodNames = typeof(IBackupService)
            .GetMethods()
            .Select(static method => method.Name)
            .ToArray();

        CollectionAssert.DoesNotContain(methodNames, "RestoreAsync");
        CollectionAssert.DoesNotContain(
            methodNames,
            "RestoreAndPublishAsync");
        CollectionAssert.DoesNotContain(methodNames, "PreviewAsync");
        Assert.IsNull(typeof(BackupCoordinator).GetMethod(
            "CommitPreparedRestoreAsync"));
    }

    [TestMethod]
    public async Task CommitAsync_source置換後もpreview済みstageを復元する()
    {
        await using RestoreWorkflowTestStore sourceA =
            await RestoreWorkflowTestStore.CreateAsync(
                "confirmed",
                startupEnabled: false);
        await using RestoreWorkflowTestStore sourceB =
            await RestoreWorkflowTestStore.CreateAsync(
                "replaced",
                startupEnabled: false);
        string selectedPath = await sourceA.ExportAsync();
        string replacementPath = await sourceB.ExportAsync();
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
                "old",
                startupEnabled: false);
        GameManager manager = destination.CreateManager();
        DataEnvelope current = (await destination.Store.LoadAsync(
            CancellationToken.None)).Envelope!;
        await manager.InitializeAsync(current, CancellationToken.None);
        RestoreCoordinator restore = new(
            destination.CreateBackup(),
            manager);

        PreparedBackupRestore prepared = await restore.PrepareAsync(
            selectedPath,
            CancellationToken.None);
        File.Copy(replacementPath, selectedPath, overwrite: true);

        BackupRestoreResult result = await restore.CommitPreparedAsync(
            prepared.SessionId,
            isReplacementConfirmed: true,
            CancellationToken.None);

        Assert.IsTrue(result.IsCommitted);
        Assert.AreEqual("confirmed", manager.Games[0].Name);
        Assert.AreEqual(
            "confirmed",
            (await destination.Store.LoadAsync(
                CancellationToken.None)).Envelope!.Games[0].Name);
    }

    [TestMethod]
    public async Task CancelAsync_preparedStageとjournalを即時削除する()
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync(
                "new",
                startupEnabled: false);
        string backupPath = await source.ExportAsync();
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
                "old",
                startupEnabled: false);
        BackupCoordinator backup = destination.CreateBackup();
        PreparedBackupRestore prepared = await backup.PrepareRestoreAsync(
            backupPath,
            CancellationToken.None);
        string stateRoot = Path.Combine(
            destination.Paths.DataRootPath,
            ".backup");
        string sentinel = Path.Combine(stateRoot, "stage", "sentinel");
        await File.WriteAllTextAsync(sentinel, "large-stage-placeholder");
        string journal = await File.ReadAllTextAsync(
            Path.Combine(stateRoot, "restore-journal.json"));
        StringAssert.Contains(journal, prepared.SessionId);
        Assert.IsFalse(journal.Contains(
            backupPath,
            StringComparison.OrdinalIgnoreCase));

        await backup.CancelPreparedRestoreAsync(
            prepared.SessionId,
            CancellationToken.None);

        Assert.IsFalse(Directory.Exists(Path.Combine(stateRoot, "stage")));
        Assert.IsFalse(Directory.Exists(
            Path.Combine(stateRoot, "previous.pending")));
        Assert.IsFalse(File.Exists(
            Path.Combine(stateRoot, "restore-journal.json")));
    }

    [TestMethod]
    public async Task PrepareAsync_Staged例外時はsource情報と全stageを残さない()
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync(
                "new",
                startupEnabled: false);
        string backupPath = await source.ExportAsync();
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
                "old",
                startupEnabled: false);
        BackupCoordinator backup = destination.CreateBackup(stage =>
        {
            if (stage == RestoreJournalStage.Staged)
            {
                throw new InvalidOperationException("prepare failure");
            }
        });

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            backup.PrepareRestoreAsync(
                backupPath,
                CancellationToken.None));

        string stateRoot = Path.Combine(
            destination.Paths.DataRootPath,
            ".backup");
        Assert.IsFalse(Directory.Exists(Path.Combine(stateRoot, "stage")));
        Assert.IsFalse(File.Exists(
            Path.Combine(stateRoot, "restore-journal.json")));
        if (Directory.Exists(stateRoot))
        {
            foreach (string file in Directory.EnumerateFiles(
                stateRoot,
                "*",
                SearchOption.AllDirectories))
            {
                Assert.IsFalse((await File.ReadAllTextAsync(file)).Contains(
                    backupPath,
                    StringComparison.OrdinalIgnoreCase));
            }
        }
    }

    [TestMethod]
    public async Task CommitAsync_Assets切替中のcleanupとsaveを直列化する()
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync(
                "new",
                startupEnabled: false);
        string restoredAssetId = Guid.NewGuid().ToString("N");
        byte[] image = await ReadValidPngAsync();
        string sourceAssets = Path.Combine(
            source.Paths.DataRootPath,
            "Assets");
        Directory.CreateDirectory(sourceAssets);
        await File.WriteAllBytesAsync(
            Path.Combine(sourceAssets, restoredAssetId + ".png"),
            image);
        DataEnvelope sourceData = (await source.Store.LoadAsync(
            CancellationToken.None)).Envelope!;
        await source.Store.SaveAsync(
            sourceData with
            {
                Games = sourceData.Games.SetItem(
                    0,
                    sourceData.Games[0] with
                    {
                        ImageAssetId = restoredAssetId,
                    }),
            },
            CancellationToken.None);
        string backupPath = await source.ExportAsync();

        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
                "old",
                startupEnabled: false);
        AssetStore assetStore = new(destination.Paths);
        await using MemoryStream saveSource = new(image, writable: false);
        Task? cleanupTask = null;
        Task<StoredAsset>? saveTask = null;
        BackupCoordinator backup = destination.CreateBackup(stage =>
        {
            if (stage != RestoreJournalStage.LocalCommitted)
            {
                return;
            }

            cleanupTask = assetStore.DeleteOrphansAfterCommitAsync(
                new HashSet<string>(StringComparer.Ordinal)
                {
                    restoredAssetId,
                },
                CancellationToken.None);
            saveTask = assetStore.SaveAsync(
                saveSource,
                "concurrent.png",
                CancellationToken.None);
            Assert.IsFalse(cleanupTask.IsCompleted);
            Assert.IsFalse(saveTask.IsCompleted);
        });
        PreparedBackupRestore prepared = await backup.PrepareRestoreAsync(
            backupPath,
            CancellationToken.None);
        GameManager manager = destination.CreateManager();
        await manager.InitializeAsync(
            (await destination.Store.LoadAsync(
                CancellationToken.None)).Envelope!,
            CancellationToken.None);
        RestoreCoordinator restore = new(backup, manager);

        _ = await restore.CommitPreparedAsync(
                prepared.SessionId,
                isReplacementConfirmed: true,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));
        await cleanupTask!.WaitAsync(TimeSpan.FromSeconds(5));
        StoredAsset saved = await saveTask!.WaitAsync(
            TimeSpan.FromSeconds(5));

        string destinationAssets = Path.Combine(
            destination.Paths.DataRootPath,
            "Assets");
        Assert.IsTrue(File.Exists(Path.Combine(
            destinationAssets,
            restoredAssetId + ".png")));
        Assert.IsTrue(File.Exists(saved.FilePath));
        DataEnvelope restored = (await destination.Store.LoadAsync(
            CancellationToken.None)).Envelope!;
        Assert.AreEqual(restoredAssetId, restored.Games[0].ImageAssetId);
    }

    [TestMethod]
    public async Task ResumeAsync_processCrashのprecommit残骸をcleanupする()
    {
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
                "old",
                startupEnabled: false);
        string stateRoot = Path.Combine(
            destination.Paths.DataRootPath,
            ".backup");
        string stage = Path.Combine(stateRoot, "stage");
        string pending = Path.Combine(stateRoot, "previous.pending");
        Directory.CreateDirectory(Path.Combine(stage, "Assets"));
        Directory.CreateDirectory(pending);
        await File.WriteAllTextAsync(
            Path.Combine(stage, "data.json"),
            "sentinel-stage");
        await File.WriteAllTextAsync(
            Path.Combine(pending, "sentinel"),
            "sentinel-pending");
        BackupTransactionStore transactions = new(destination.Paths);
        await transactions.WriteJournalAsync(
            new RestoreJournal(
                RestoreJournalStage.Staged,
                Guid.NewGuid().ToString("N"),
                RequiresDerivedStateRetry: false),
            CancellationToken.None);

        BackupRestoreResult? resumed =
            await destination.CreateBackup().ResumeAsync(
                CancellationToken.None);

        Assert.IsNull(resumed);
        Assert.IsFalse(Directory.Exists(stage));
        Assert.IsFalse(Directory.Exists(pending));
        Assert.IsFalse(File.Exists(
            Path.Combine(stateRoot, "restore-journal.json")));
    }

    [TestMethod]
    public async Task ResumeAsync_completedJournalはcleanupだけ行いnullを返す()
    {
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
                "current",
                startupEnabled: false);
        string stateRoot = Path.Combine(
            destination.Paths.DataRootPath,
            ".backup");
        string stage = Path.Combine(stateRoot, "stage");
        string pending = Path.Combine(stateRoot, "previous.pending");
        string rollback = Path.Combine(stateRoot, "assets.rollback");
        Directory.CreateDirectory(stage);
        Directory.CreateDirectory(pending);
        Directory.CreateDirectory(rollback);
        await File.WriteAllTextAsync(
            Path.Combine(stage, "sentinel"),
            "cleanup-after-ack-crash");
        BackupTransactionStore transactions = new(destination.Paths);
        await transactions.WriteJournalAsync(
            new RestoreJournal(
                RestoreJournalStage.Completed,
                Guid.NewGuid().ToString("N"),
                RequiresDerivedStateRetry: false),
            CancellationToken.None);

        BackupRestoreResult? resumed =
            await destination.CreateBackup().ResumeAsync(
                CancellationToken.None);

        Assert.IsNull(resumed);
        Assert.IsFalse(Directory.Exists(stage));
        Assert.IsFalse(Directory.Exists(pending));
        Assert.IsFalse(Directory.Exists(rollback));
        Assert.IsFalse(File.Exists(
            Path.Combine(stateRoot, "restore-journal.json")));
    }

    [TestMethod]
    public async Task ExportAsync_sharedAssetGate解放までassetを読まない()
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync(
                "current",
                startupEnabled: false);
        SemaphoreSlim assetGate = AppAssetGate.Get(source.Paths);
        await assetGate.WaitAsync(CancellationToken.None);
        string path = Path.Combine(source.Root, "blocked.staminabackup");
        Task exportTask = source.CreateBackup().ExportAsync(
            path,
            CancellationToken.None);
        try
        {
            await Task.Delay(50);
            Assert.IsFalse(exportTask.IsCompleted);
            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            assetGate.Release();
        }

        await exportTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(File.Exists(path));
    }

    private static async Task<byte[]> ReadValidPngAsync()
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "Images",
            "valid-1x1.png.base64");
        return Convert.FromBase64String(await File.ReadAllTextAsync(path));
    }
}
