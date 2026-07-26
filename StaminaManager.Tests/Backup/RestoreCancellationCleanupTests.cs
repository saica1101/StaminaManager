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
