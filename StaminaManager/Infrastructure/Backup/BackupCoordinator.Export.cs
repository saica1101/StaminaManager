using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using StaminaManager.Infrastructure.Persistence;
using System.IO.Compression;
using System.Text.Json;

namespace StaminaManager.Infrastructure.Backup;

public sealed partial class BackupCoordinator
{
    private const string DataFileName = "data.json";

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
            await _dataWriteGate.WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                await _assetGate.WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
                try
                {
                    DataLoadResult loaded = await _dataStore.LoadAsync(
                        cancellationToken).ConfigureAwait(false);
                    DataEnvelope data = loaded.Envelope
                        ?? throw new InvalidOperationException(
                            "バックアップ対象のデータがありません。");
                    IReadOnlyList<ExportAsset> assets =
                        ResolveExportAssets(data);
                    Directory.CreateDirectory(
                        Path.GetDirectoryName(fullDestination)
                        ?? throw new InvalidOperationException(
                            "The destination directory is invalid."));
                    await WriteArchiveAsync(
                        temporaryPath,
                        data,
                        assets,
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
