using StaminaManager.Infrastructure.Persistence;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace StaminaManager.Infrastructure.Storage;

public sealed record StoredAsset(
    string AssetId,
    string FilePath,
    string MediaType);

public sealed class AssetValidationException : IOException
{
    public AssetValidationException(string message)
        : base(message)
    {
    }

    public AssetValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class AssetStore
{
    public const uint MaxDimension = 4096;

    private const string AssetsDirectoryName = "Assets";
    private const int CopyBufferSize = 80 * 1024;
    private readonly SemaphoreSlim _operationGate;
    private readonly IAppDataPathProvider _pathProvider;

    public AssetStore(IAppDataPathProvider pathProvider)
    {
        ArgumentNullException.ThrowIfNull(pathProvider);
        _pathProvider = pathProvider;
        _operationGate = AppAssetGate.Get(pathProvider);
    }

    public async Task<StoredAsset> SaveAsync(
        Stream source,
        string originalFileName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(originalFileName);
        if (!source.CanRead)
        {
            throw new ArgumentException(
                "The source stream must be readable.",
                nameof(source));
        }

        await _operationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            return await SaveCoreAsync(source, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<StoredAsset> SaveCoreAsync(
        Stream source,
        CancellationToken cancellationToken)
    {
        string assetsPath = GetAssetsPath();
        Directory.CreateDirectory(assetsPath);
        string inputPath = Path.Combine(
            assetsPath,
            $"{Guid.NewGuid():N}.input.tmp");
        bool ownsInput = false;
        try
        {
            await CopyInputAsync(
                source,
                inputPath,
                cancellationToken).ConfigureAwait(false);
            ownsInput = true;
            StorageFile inputFile = await StorageFile.GetFileFromPathAsync(
                    inputPath)
                .AsTask(cancellationToken).ConfigureAwait(false);
            using IRandomAccessStreamWithContentType input =
                await inputFile.OpenReadAsync()
                    .AsTask(cancellationToken).ConfigureAwait(false);

            ImageFormat format;
            SoftwareBitmap bitmap;
            try
            {
                BitmapDecoder decoder = await BitmapDecoder.CreateAsync(input)
                    .AsTask(cancellationToken).ConfigureAwait(false);
                format = GetFormat(decoder.DecoderInformation.CodecId);
                ValidateDimensions(decoder.PixelWidth, decoder.PixelHeight);
                BitmapAlphaMode alphaMode = format == ImageFormat.Jpeg
                    ? BitmapAlphaMode.Ignore
                    : BitmapAlphaMode.Premultiplied;
                bitmap = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8,
                        alphaMode)
                    .AsTask(cancellationToken).ConfigureAwait(false);
            }
            catch (AssetValidationException)
            {
                throw;
            }
            catch (Exception exception) when (IsImagingFailure(exception))
            {
                throw new AssetValidationException(
                    "The source is not a valid PNG or JPEG image.",
                    exception);
            }

            using (bitmap)
            using (InMemoryRandomAccessStream encoded = new())
            {
                try
                {
                    BitmapEncoder encoder = await BitmapEncoder.CreateAsync(
                            format.EncoderId,
                            encoded)
                        .AsTask(cancellationToken).ConfigureAwait(false);
                    encoder.SetSoftwareBitmap(bitmap);
                    await encoder.FlushAsync()
                        .AsTask(cancellationToken).ConfigureAwait(false);
                }
                catch (AssetValidationException)
                {
                    throw;
                }
                catch (Exception exception) when (IsImagingFailure(exception))
                {
                    throw new AssetValidationException(
                        "The image could not be normalized.",
                        exception);
                }

                return await SaveEncodedAsync(
                    encoded,
                    format,
                    cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (ownsInput && File.Exists(inputPath))
            {
                File.Delete(inputPath);
            }
        }
    }

    public async Task DeleteOrphansAfterCommitAsync(
        IReadOnlySet<string> referencedAssetIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(referencedAssetIds);
        await _operationGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            string assetsPath = GetAssetsPath();
            if (!Directory.Exists(assetsPath))
            {
                return;
            }

            foreach (string path in Directory.EnumerateFiles(
                assetsPath,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryParseOwnedAssetPath(
                    path,
                    out string? assetId,
                    out bool isTemporary))
                {
                    continue;
                }

                if (isTemporary || !referencedAssetIds.Contains(assetId))
                {
                    File.Delete(path);
                }
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<StoredAsset> SaveEncodedAsync(
        InMemoryRandomAccessStream encoded,
        ImageFormat format,
        CancellationToken cancellationToken)
    {
        string assetId = Guid.NewGuid().ToString("N");
        string assetsPath = GetAssetsPath();
        Directory.CreateDirectory(assetsPath);
        string fileName = $"{assetId}.{format.Extension}";
        string finalPath = Path.Combine(assetsPath, fileName);
        string temporaryPath = $"{finalPath}.tmp";
        bool ownsTemporaryFile = false;
        try
        {
            encoded.Seek(0);
            await using (FileStream output = new(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous))
            {
                ownsTemporaryFile = true;
                await CopyEncodedAsync(
                    encoded,
                    output,
                    cancellationToken).ConfigureAwait(false);
                await output.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, finalPath);
            return new StoredAsset(assetId, finalPath, format.MediaType);
        }
        finally
        {
            if (ownsTemporaryFile && File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task CopyInputAsync(
        Stream source,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        bool ownsDestination = false;
        try
        {
            await using FileStream destination = new(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                CopyBufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            ownsDestination = true;
            await source.CopyToAsync(
                destination,
                CopyBufferSize,
                cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken)
                .ConfigureAwait(false);
            destination.Flush(flushToDisk: true);
        }
        catch
        {
            if (ownsDestination && File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            throw;
        }
    }

    private static async Task CopyEncodedAsync(
        InMemoryRandomAccessStream encoded,
        FileStream output,
        CancellationToken cancellationToken)
    {
        using DataReader reader = new(encoded.GetInputStreamAt(0));
        byte[] buffer = new byte[CopyBufferSize];
        ulong totalBytesRead = 0;
        while (totalBytesRead < encoded.Size)
        {
            uint requested = checked((uint)Math.Min(
                (ulong)buffer.Length,
                encoded.Size - totalBytesRead));
            uint loaded = await reader.LoadAsync(requested)
                .AsTask(cancellationToken).ConfigureAwait(false);
            if (loaded == 0)
            {
                throw new EndOfStreamException(
                    "The encoded image stream ended unexpectedly.");
            }

            byte[] destination = loaded == buffer.Length
                ? buffer
                : new byte[loaded];
            reader.ReadBytes(destination);
            await output.WriteAsync(
                destination.AsMemory(0, checked((int)loaded)),
                cancellationToken).ConfigureAwait(false);
            totalBytesRead += loaded;
        }
    }

    private static ImageFormat GetFormat(Guid decoderId)
    {
        if (decoderId == BitmapDecoder.PngDecoderId)
        {
            return ImageFormat.Png;
        }

        if (decoderId == BitmapDecoder.JpegDecoderId)
        {
            return ImageFormat.Jpeg;
        }

        throw new AssetValidationException(
            "Only PNG and JPEG images are supported.");
    }

    private static void ValidateDimensions(uint width, uint height)
    {
        if (width == 0
            || height == 0
            || width > MaxDimension
            || height > MaxDimension)
        {
            throw new AssetValidationException(
                "Image dimensions must be between 1 and 4096 pixels.");
        }
    }

    private static bool IsImagingFailure(Exception exception) =>
        exception is COMException
            or ArgumentException
            or InvalidOperationException;

    private static bool TryParseOwnedAssetPath(
        string path,
        out string assetId,
        out bool isTemporary)
    {
        string fileName = Path.GetFileName(path);
        if (fileName.EndsWith(".input.tmp", StringComparison.Ordinal))
        {
            string inputId = fileName[..^".input.tmp".Length];
            isTemporary = Guid.TryParseExact(
                inputId,
                "N",
                out Guid parsedInput)
                && string.Equals(
                    inputId,
                    parsedInput.ToString("N"),
                    StringComparison.Ordinal);
            assetId = string.Empty;
            return isTemporary;
        }

        isTemporary = fileName.EndsWith(".tmp", StringComparison.Ordinal);
        string assetFileName = isTemporary
            ? fileName[..^".tmp".Length]
            : fileName;
        string extension = Path.GetExtension(assetFileName);
        if (extension is not ".png" and not ".jpg")
        {
            assetId = string.Empty;
            return false;
        }

        assetId = Path.GetFileNameWithoutExtension(assetFileName);
        return Guid.TryParseExact(assetId, "N", out Guid parsed)
            && string.Equals(
                assetId,
                parsed.ToString("N"),
                StringComparison.Ordinal);
    }

    private string GetAssetsPath() => Path.Combine(
        _pathProvider.DataRootPath,
        AssetsDirectoryName);

    private sealed record ImageFormat(
        Guid EncoderId,
        string Extension,
        string MediaType)
    {
        public static ImageFormat Png { get; } = new(
            BitmapEncoder.PngEncoderId,
            "png",
            "image/png");

        public static ImageFormat Jpeg { get; } = new(
            BitmapEncoder.JpegEncoderId,
            "jpg",
            "image/jpeg");
    }
}
