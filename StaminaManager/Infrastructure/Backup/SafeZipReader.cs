using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using StaminaManager.Infrastructure.Persistence;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace StaminaManager.Infrastructure.Backup;

public sealed record ValidatedBackupAsset(
    string AssetId,
    string EntryPath,
    string MediaType,
    byte[] Content);

public sealed record ValidatedBackup(
    DataEnvelope Data,
    ImmutableArray<ValidatedBackupAsset> Assets,
    BackupPreview Preview);

public sealed class SafeZipReader
{
    private const string ManifestEntryName = "manifest.json";
    private const int CopyBufferSize = 80 * 1024;

    public async Task<ValidatedBackup> ReadAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
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
        BackupManifest manifest =
            BackupArchiveValidator.DeserializeManifest(manifestBytes);
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
        DataEnvelope data = BackupArchiveValidator.DeserializeData(dataBytes);
        BackupArchiveValidator.ValidateData(data);

        ImmutableArray<ValidatedBackupAsset> assets =
            await ReadAssetsAsync(
                manifest,
                entries,
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

    private static async Task<ImmutableArray<ValidatedBackupAsset>>
        ReadAssetsAsync(
            BackupManifest manifest,
            IReadOnlyDictionary<string, ZipArchiveEntry> entries,
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

            byte[] content = await ReadBoundedAsync(
                entry,
                BackupLimits.MaxImageBytes,
                cancellationToken).ConfigureAwait(false);
            await BackupArchiveValidator.ValidateImageAsync(
                content,
                asset.MediaType,
                cancellationToken).ConfigureAwait(false);
            builder.Add(new ValidatedBackupAsset(
                asset.AssetId,
                asset.EntryPath,
                asset.MediaType,
                content));
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

    private static ZipArchiveEntry GetRequiredEntry(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        string name) => entries.TryGetValue(name, out ZipArchiveEntry? entry)
            ? entry
            : throw new InvalidDataException(
                $"The required entry '{name}' is missing.");

}
