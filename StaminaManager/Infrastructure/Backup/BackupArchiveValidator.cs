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

internal static class BackupArchiveValidator
{
    public static Dictionary<string, ZipArchiveEntry> ValidateMetadata(
        ZipArchive archive)
    {
        if (archive.Entries.Count > BackupLimits.MaxEntryCount)
        {
            throw new InvalidDataException(
                "The backup contains too many entries.");
        }

        Dictionary<string, ZipArchiveEntry> entries =
            new(StringComparer.OrdinalIgnoreCase);
        long expandedTotal = 0;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            ValidateEntryPath(entry.FullName);
            if (!entries.TryAdd(entry.FullName, entry))
            {
                throw new InvalidDataException(
                    "The backup contains duplicate entries.");
            }

            expandedTotal = checked(expandedTotal + entry.Length);
            if (expandedTotal > BackupLimits.MaxExpandedBytes)
            {
                throw new InvalidDataException(
                    "The expanded backup is too large.");
            }

            if (entry.CompressedLength == 0)
            {
                if (entry.Length > 0)
                {
                    throw new InvalidDataException(
                        "A non-empty entry has no compressed data.");
                }

                continue;
            }

            if ((double)entry.Length / entry.CompressedLength
                > BackupLimits.MaxCompressionRatio)
            {
                throw new InvalidDataException(
                    "An entry compression ratio is too high.");
            }
        }

        return entries;
    }

    public static BackupManifest DeserializeManifest(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize<BackupManifest>(
                bytes,
                SerializerOptions)
                ?? throw new InvalidDataException("The manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The manifest is invalid.",
                exception);
        }
    }

    public static DataEnvelope DeserializeData(byte[] bytes)
    {
        try
        {
            return JsonSerializer.Deserialize(
                bytes,
                JsonSerializationContext.Configured.DataEnvelope)
                ?? throw new InvalidDataException(
                    "The data JSON is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The data JSON is invalid.",
                exception);
        }
    }

    public static void ValidateManifest(BackupManifest manifest)
    {
        if (manifest.SchemaVersion != BackupManifest.CurrentSchemaVersion
            || manifest.DataSchemaVersion
                != DataEnvelope.CurrentSchemaVersion
            || !string.Equals(
                manifest.DataFile,
                "data.json",
                StringComparison.Ordinal)
            || manifest.Assets is null
            || manifest.Assets.Count > BackupLimits.MaxImageCount)
        {
            throw new InvalidDataException(
                "The backup schema is not supported.");
        }

        HashSet<string> ids = new(StringComparer.Ordinal);
        HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
        foreach (BackupAssetManifest? asset in manifest.Assets)
        {
            if (asset is null
                || !IsCanonicalAssetId(asset.AssetId)
                || !paths.Add(asset.EntryPath)
                || !ids.Add(asset.AssetId)
                || !IsCanonicalAssetEntry(asset)
                || asset.MediaType is not "image/png" and not "image/jpeg")
            {
                throw new InvalidDataException(
                    "The asset manifest is invalid.");
            }
        }
    }

    public static void ValidateData(DataEnvelope data)
    {
        if (data.SchemaVersion != DataEnvelope.CurrentSchemaVersion
            || data.Games.IsDefault
            || data.Games.Length > GameEntryValidator.MaxGameCount
            || data.Settings is null
            || !Enum.IsDefined(data.Settings.Theme)
            || !Enum.IsDefined(data.Settings.Backdrop)
            || !Enum.IsDefined(data.Settings.CloseBehavior)
            || !Enum.IsDefined(data.Settings.LastDisplayMode)
            || data.Settings.NotificationLeadMinutes
                is < AppSettings.MinNotificationLeadMinutes
                    or > AppSettings.MaxNotificationLeadMinutes)
        {
            throw new InvalidDataException("The backup data is invalid.");
        }

        HashSet<Guid> gameIds = [];
        HashSet<int> sortOrders = [];
        foreach (GameEntry? game in data.Games)
        {
            if (game is null
                || game.Id == Guid.Empty
                || !gameIds.Add(game.Id)
                || game.SortOrder < 0
                || game.SortOrder >= data.Games.Length
                || !sortOrders.Add(game.SortOrder)
                || game.ImageAssetId is not null
                    && !IsCanonicalAssetId(game.ImageAssetId))
            {
                throw new InvalidDataException("A game entry is invalid.");
            }

            ValidationResult validation = GameEntryValidator.Validate(
                new GameDraft(
                    game.Name,
                    game.BaseStamina,
                    game.MaxStamina,
                    game.RecoveryMinutes,
                    game.ImageAssetId),
                game.RecordedAtUtc.ToUniversalTime());
            if (!validation.IsValid)
            {
                throw new InvalidDataException("A game entry is invalid.");
            }
        }

        if (data.Settings.SelectedCompactGameId is Guid selected
            && !gameIds.Contains(selected))
        {
            throw new InvalidDataException(
                "The selected game does not exist.");
        }
    }

    public static async Task ValidateImageFileAsync(
        string filePath,
        string mediaType,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        try
        {
            global::Windows.Storage.StorageFile file =
                await global::Windows.Storage.StorageFile.GetFileFromPathAsync(
                        Path.GetFullPath(filePath))
                    .AsTask(cancellationToken).ConfigureAwait(false);
            using IRandomAccessStreamWithContentType input =
                await file.OpenReadAsync().AsTask(cancellationToken)
                    .ConfigureAwait(false);
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(input)
                .AsTask(cancellationToken).ConfigureAwait(false);
            Guid expectedCodec = mediaType == "image/png"
                ? BitmapDecoder.PngDecoderId
                : BitmapDecoder.JpegDecoderId;
            if (decoder.DecoderInformation.CodecId != expectedCodec
                || decoder.PixelWidth is 0
                    or > BackupLimits.MaxImageDimension
                || decoder.PixelHeight is 0
                    or > BackupLimits.MaxImageDimension)
            {
                throw new InvalidDataException(
                    "A backup image is invalid.");
            }

            using SoftwareBitmap decoded =
                await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Ignore)
                    .AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is COMException
                or ArgumentException
                or InvalidOperationException)
        {
            throw new InvalidDataException(
                "A backup image is invalid.",
                exception);
        }
    }

    public static void ValidateContents(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        BackupManifest manifest)
    {
        HashSet<string> expected = new(StringComparer.OrdinalIgnoreCase)
        {
            "manifest.json",
            manifest.DataFile,
        };
        expected.UnionWith(manifest.Assets.Select(static asset =>
            asset.EntryPath));
        if (entries.Count != expected.Count
            || entries.Keys.Any(path => !expected.Contains(path)))
        {
            throw new InvalidDataException(
                "The backup contains undeclared entries.");
        }
    }

    public static void ValidateAssetReferences(
        DataEnvelope data,
        ImmutableArray<ValidatedBackupAsset> assets)
    {
        HashSet<string> expected = data.Games
            .Select(static game => game.ImageAssetId)
            .Where(static assetId => assetId is not null)
            .Select(static assetId => assetId!)
            .ToHashSet(StringComparer.Ordinal);
        HashSet<string> actual = assets
            .Select(static asset => asset.AssetId)
            .ToHashSet(StringComparer.Ordinal);
        if (!expected.SetEquals(actual))
        {
            throw new InvalidDataException(
                "The backup assets do not match the game data.");
        }
    }

    private static void ValidateEntryPath(string entryPath)
    {
        if (string.IsNullOrWhiteSpace(entryPath)
            || entryPath.Length > BackupLimits.MaxEntryPathLength
            || entryPath.Contains('\\', StringComparison.Ordinal)
            || entryPath.Contains(':', StringComparison.Ordinal)
            || Path.IsPathRooted(entryPath))
        {
            throw new InvalidDataException("An entry path is invalid.");
        }

        string root = Path.Combine(
            Path.GetTempPath(),
            "StaminaManagerBackupValidation");
        string rootPrefix = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string candidate = Path.GetFullPath(Path.Combine(
            root,
            entryPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!candidate.StartsWith(rootPrefix, StringComparison.Ordinal)
            || entryPath.Split('/').Any(static segment =>
                segment.Length == 0 || segment is "." or ".."))
        {
            throw new InvalidDataException(
                "An entry path escapes the backup root.");
        }
    }

    private static bool IsCanonicalAssetId(string value) =>
        Guid.TryParseExact(value, "N", out Guid parsed)
        && string.Equals(value, parsed.ToString("N"), StringComparison.Ordinal);

    private static bool IsCanonicalAssetEntry(BackupAssetManifest asset)
    {
        string extension = asset.MediaType == "image/png" ? ".png" : ".jpg";
        return string.Equals(
            asset.EntryPath,
            $"assets/{asset.AssetId}{extension}",
            StringComparison.Ordinal);
    }

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        RespectRequiredConstructorParameters = true,
    };
}
