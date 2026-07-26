using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Backup;

namespace StaminaManager.Tests.Backup;

[TestClass]
public sealed class RestoreCancellationCleanupTests
{
    [TestMethod]
    public async Task PrepareRestoreAsync_assetStreaming取消時はstageとjournalを削除する()
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync(
                "new",
                startupEnabled: false);
        string assetId = Guid.NewGuid().ToString("N");
        byte[] image = await ReadValidPngAsync();
        string assets = Path.Combine(source.Paths.DataRootPath, "Assets");
        Directory.CreateDirectory(assets);
        await File.WriteAllBytesAsync(
            Path.Combine(assets, assetId + ".png"),
            image);
        DataEnvelope data = (await source.Store.LoadAsync(
            CancellationToken.None)).Envelope!;
        await source.Store.SaveAsync(
            data with
            {
                Games = data.Games.SetItem(
                    0,
                    data.Games[0] with { ImageAssetId = assetId }),
            },
            CancellationToken.None);
        string backupPath = await source.ExportAsync();

        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
                "old",
                startupEnabled: false);
        using CancellationTokenSource cancellation = new();
        SafeZipReader reader = new(bufferedBytes =>
        {
            if (bufferedBytes == 80 * 1024)
            {
                cancellation.Cancel();
            }
        });
        BackupCoordinator backup = new(
            reader,
            destination.Store,
            destination.Paths);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            backup.PrepareRestoreAsync(
                backupPath,
                cancellation.Token));

        string state = Path.Combine(
            destination.Paths.DataRootPath,
            ".backup");
        Assert.IsFalse(Directory.Exists(Path.Combine(state, "stage")));
        Assert.IsFalse(Directory.Exists(
            Path.Combine(state, "previous.pending")));
        Assert.IsFalse(File.Exists(
            Path.Combine(state, "restore-journal.json")));
    }

    [TestMethod]
    public async Task ResumeAsync_journal作成前に停止した場合は残留stageを削除する()
    {
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
                "old",
                startupEnabled: false);
        string state = Path.Combine(
            destination.Paths.DataRootPath,
            ".backup");
        string stageAssets = Path.Combine(state, "stage", "Assets");
        string pending = Path.Combine(state, "previous.pending");
        Directory.CreateDirectory(stageAssets);
        Directory.CreateDirectory(pending);
        await File.WriteAllTextAsync(
            Path.Combine(stageAssets, "orphan.png"),
            "orphan");
        await File.WriteAllTextAsync(
            Path.Combine(pending, "orphan.json"),
            "orphan");

        BackupRestoreResult? result = await destination.CreateBackup().ResumeAsync(
            CancellationToken.None);

        Assert.IsNull(result);
        Assert.IsFalse(Directory.Exists(Path.Combine(state, "stage")));
        Assert.IsFalse(Directory.Exists(pending));
        Assert.IsFalse(File.Exists(
            Path.Combine(state, "restore-journal.json")));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("{broken")]
    public async Task ResumeAsync_precommitのemptyOrCorruptJournalを隔離してcleanupする(
        string journalContent)
    {
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync("old", false);
        string state = Path.Combine(destination.Paths.DataRootPath, ".backup");
        Directory.CreateDirectory(Path.Combine(state, "stage", "Assets"));
        await File.WriteAllTextAsync(
            Path.Combine(state, "stage", "data.json"),
            "staged");
        await File.WriteAllTextAsync(
            Path.Combine(state, "restore-journal.json"),
            journalContent);

        BackupRestoreResult? result = await destination.CreateBackup()
            .ResumeAsync(CancellationToken.None);

        Assert.IsNull(result);
        Assert.AreEqual(
            "old",
            (await destination.Store.LoadAsync(CancellationToken.None))
                .Envelope!.Games[0].Name);
        Assert.IsFalse(Directory.Exists(Path.Combine(state, "stage")));
        Assert.HasCount(
            1,
            Directory.GetFiles(state, "restore-journal.corrupt-*.json"));
        Assert.IsNull(await destination.CreateBackup().ResumeAsync(
            CancellationToken.None));
    }

    [TestMethod]
    public async Task ResumeAsync_postcommitのcorruptJournalはprimaryを正としてmemoryを復元する()
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync("new", false);
        string backupPath = await source.ExportAsync();
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync("old", false);
        GameManager manager = destination.CreateManager();
        await manager.InitializeAsync(
            (await destination.Store.LoadAsync(CancellationToken.None)).Envelope!,
            CancellationToken.None);
        BackupCoordinator backup = destination.CreateBackup();
        _ = await backup.PrepareRestoreAsync(
            backupPath,
            CancellationToken.None);
        BackupTransactionStore transactions = new(destination.Paths);
        await transactions.CommitAsync(CancellationToken.None);
        string state = Path.Combine(destination.Paths.DataRootPath, ".backup");
        await File.WriteAllTextAsync(
            Path.Combine(state, "restore-journal.json"),
            "{broken");

        BackupRestoreResult? result = await new RestoreCoordinator(
                backup,
                manager)
            .ResumeAsync(CancellationToken.None);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.IsCommitted);
        Assert.IsTrue(result.IsPartial);
        Assert.AreEqual("new", manager.Games[0].Name);
        Assert.HasCount(
            1,
            Directory.GetFiles(state, "restore-journal.corrupt-*.json"));
        Assert.IsNotNull(await backup.ResumeAsync(CancellationToken.None));
    }

    [TestMethod]
    public async Task ResumeAsync_corruptJournal再構築失敗後も次回起動でrecoverできる()
    {
        await using RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync("new", false);
        string backupPath = await source.ExportAsync();
        await using RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync("old", false);
        BackupCoordinator preparing = destination.CreateBackup();
        _ = await preparing.PrepareRestoreAsync(backupPath, CancellationToken.None);
        await new BackupTransactionStore(destination.Paths).CommitAsync(
            CancellationToken.None);
        string journal = Path.Combine(
            destination.Paths.DataRootPath,
            ".backup",
            "restore-journal.json");
        await File.WriteAllTextAsync(journal, "{broken");
        BackupCoordinator recovering = destination.CreateBackup(
            journalWriteInjector: stage =>
            {
                if (stage == RestoreJournalStage.LocalCommitted)
                {
                    throw new IOException("disk full");
                }
            });

        Assert.IsNotNull(await recovering.ResumeAsync(CancellationToken.None));
        Assert.IsNotNull(await recovering.ResumeAsync(CancellationToken.None));
        Assert.AreEqual(
            "new",
            (await destination.Store.LoadAsync(CancellationToken.None))
                .Envelope!.Games[0].Name);
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
