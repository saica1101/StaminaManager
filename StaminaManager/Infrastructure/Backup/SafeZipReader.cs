using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.Json;

namespace StaminaManager.Infrastructure.Backup;

public sealed record ValidatedBackupAsset(
    string AssetId,
    string EntryPath,
    string MediaType,
    string StagedFilePath,
    long Length);

public sealed record ValidatedBackup(
    DataEnvelope Data,
    ImmutableArray<ValidatedBackupAsset> Assets,
    BackupPreview Preview);

public sealed class SafeZipReader
{
    private const string ManifestEntryName = "manifest.json";
    private const int CopyBufferSize = 80 * 1024;
    private readonly Action<int>? _bufferedBytesObserver;

    public SafeZipReader(Action<int>? bufferedBytesObserver = null)
    {
        _bufferedBytesObserver = bufferedBytesObserver;
    }

    public async Task<ValidatedBackup> ReadAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        string temporaryRoot = Path.Combine(
            Path.GetTempPath(),
            "StaminaManager.BackupValidation",
            Guid.NewGuid().ToString("N"));
        string assets = Path.Combine(temporaryRoot, "Assets");
        Directory.CreateDirectory(assets);
        try
        {
            return await ReadToStageAsync(
                    source,
                    assets,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            if (Directory.Exists(temporaryRoot))
            {
                Directory.Delete(temporaryRoot, recursive: true);
            }
        }
    }

    public async Task<ValidatedBackup> ReadToStageAsync(
        Stream source,
        string stagedAssetsDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagedAssetsDirectory);
        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The backup stream must be readable.",
                nameof(source));
        }

        if (source.CanSeek && source.Length > BackupLimits.MaxBackupBytes)
        {
            throw new InvalidDataException("The backup file is too large.");
        }

        using ZipArchive archive = new(
            source,
            ZipArchiveMode.Read,
            leaveOpen: true);
        Dictionary<string, ZipArchiveEntry> entries =
            BackupArchiveValidator.ValidateMetadata(archive);
        ZipArchiveEntry manifestEntry = GetRequiredEntry(
            entries,
            ManifestEntryName);
        if (manifestEntry.Length > BackupLimits.MaxManifestBytes)
        {
            throw new InvalidDataException("The manifest is too large.");
        }

        byte[] manifestBytes = await ReadBoundedAsync(
            manifestEntry,
            BackupLimits.MaxManifestBytes,
            cancellationToken).ConfigureAwait(false);
        _bufferedBytesObserver?.Invoke(manifestBytes.Length);
        BackupManifest manifest =
            BackupArchiveValidator.DeserializeManifest(manifestBytes);
        manifestBytes = [];
        _bufferedBytesObserver?.Invoke(0);
        BackupArchiveValidator.ValidateManifest(manifest);

        ZipArchiveEntry dataEntry = GetRequiredEntry(
            entries,
            manifest.DataFile);
        if (dataEntry.Length > BackupLimits.MaxDataJsonBytes)
        {
            throw new InvalidDataException("The data JSON is too large.");
        }

        byte[] dataBytes = await ReadBoundedAsync(
            dataEntry,
            BackupLimits.MaxDataJsonBytes,
            cancellationToken).ConfigureAwait(false);
        _bufferedBytesObserver?.Invoke(dataBytes.Length);
        DataEnvelope data = BackupArchiveValidator.DeserializeData(dataBytes);
        dataBytes = [];
        _bufferedBytesObserver?.Invoke(0);
        BackupArchiveValidator.ValidateData(data);

        ImmutableArray<ValidatedBackupAsset> assets =
            await ReadAssetsAsync(
                manifest,
                entries,
                stagedAssetsDirectory,
                cancellationToken).ConfigureAwait(false);
        BackupArchiveValidator.ValidateContents(entries, manifest);
        BackupArchiveValidator.ValidateAssetReferences(data, assets);

        return new ValidatedBackup(
            data,
            assets,
            BackupPreview.From(
                data.Settings,
                data.Games.Length,
                assets.Length));
    }

    private async Task<ImmutableArray<ValidatedBackupAsset>>
        ReadAssetsAsync(
            BackupManifest manifest,
            IReadOnlyDictionary<string, ZipArchiveEntry> entries,
            string stagedAssetsDirectory,
            CancellationToken cancellationToken)
    {
        ImmutableArray<ValidatedBackupAsset>.Builder builder =
            ImmutableArray.CreateBuilder<ValidatedBackupAsset>(
                manifest.Assets.Count);
        foreach (BackupAssetManifest asset in manifest.Assets)
        {
            ZipArchiveEntry entry = GetRequiredEntry(entries, asset.EntryPath);
            if (entry.Length > BackupLimits.MaxImageBytes)
            {
                throw new InvalidDataException("A backup image is too large.");
            }

            string stagedPath = Path.Combine(
                stagedAssetsDirectory,
                Path.GetFileName(asset.EntryPath));
            await CopyBoundedAsync(
                entry,
                stagedPath,
                BackupLimits.MaxImageBytes,
                cancellationToken).ConfigureAwait(false);
            await BackupArchiveValidator.ValidateImageFileAsync(
                stagedPath,
                asset.MediaType,
                cancellationToken).ConfigureAwait(false);
            builder.Add(new ValidatedBackupAsset(
                asset.AssetId,
                asset.EntryPath,
                asset.MediaType,
                stagedPath,
                entry.Length));
        }

        return builder.MoveToImmutable();
    }

    private static async Task<byte[]> ReadBoundedAsync(
        ZipArchiveEntry entry,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using Stream input = entry.Open();
        using MemoryStream output = new(
            checked((int)Math.Min(entry.Length, maxBytes)));
        byte[] buffer = new byte[CopyBufferSize];
        int total = 0;
        while (true)
        {
            int read = await input.ReadAsync(
                buffer.AsMemory(0, Math.Min(
                    buffer.Length,
                    maxBytes + 1 - total)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return output.ToArray();
            }

            total += read;
            if (total > maxBytes)
            {
                throw new InvalidDataException(
                    "A backup entry exceeds its size limit.");
            }

            await output.WriteAsync(
                buffer.AsMemory(0, read),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CopyBoundedAsync(
        ZipArchiveEntry entry,
        string destinationPath,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using Stream input = entry.Open();
        await using FileStream output = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[CopyBufferSize];
        _bufferedBytesObserver?.Invoke(buffer.Length);
        try
        {
            int total = 0;
            while (true)
            {
                int read = await input.ReadAsync(
                    buffer.AsMemory(0, Math.Min(
                        buffer.Length,
                        maxBytes + 1 - total)),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    await output.FlushAsync(cancellationToken)
                        .ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                    return;
                }

                total += read;
                if (total > maxBytes)
                {
                    throw new InvalidDataException(
                        "A backup entry exceeds its size limit.");
                }

                await output.WriteAsync(
                    buffer.AsMemory(0, read),
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _bufferedBytesObserver?.Invoke(0);
        }
    }

    private static ZipArchiveEntry GetRequiredEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string name) => entries.TryGetValue(name, out ZipArchiveEntry? entry)
            ? entry
            : throw new InvalidDataException(
                $"The required entry '{name}' is missing.");

}
