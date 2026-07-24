using StaminaManager.Infrastructure.Persistence;
using StaminaManager.Infrastructure.Storage;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace StaminaManager.Tests.Persistence;

[TestClass]
public sealed class AssetStoreTests
{
    private string _rootPath = null!;
    private AssetStore _store = null!;

    [TestInitialize]
    public void Initialize()
    {
        _rootPath = Path.Combine(
            Path.GetTempPath(),
            $"StaminaManagerAssetTests-{Guid.NewGuid():N}");
        _store = new AssetStore(new TestAppDataPathProvider(_rootPath));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }

    [TestMethod]
    [DataRow(ImageFormat.Png)]
    [DataRow(ImageFormat.Jpeg)]
    public async Task SaveAsync_ReencodesValidImageInOriginalFormat(
        ImageFormat format)
    {
        await using MemoryStream source = await CreateImageAsync(
            width: 1,
            height: 1,
            format);

        StoredAsset asset = await _store.SaveAsync(
            source,
            $"input.{GetExtension(format)}",
            CancellationToken.None);

        Assert.IsTrue(File.Exists(asset.FilePath));
        Assert.AreEqual(GetMediaType(format), asset.MediaType);
        Assert.AreEqual(GetExtension(format), Path.GetExtension(asset.FilePath)[1..]);
        using InMemoryRandomAccessStream output =
            await ReadFileAsync(asset.FilePath);
        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(output);
        Assert.AreEqual(1u, decoder.PixelWidth);
        Assert.AreEqual(1u, decoder.PixelHeight);
        Assert.AreEqual(GetDecoderId(format), decoder.DecoderInformation.CodecId);
    }

    [TestMethod]
    public async Task SaveAsync_RejectsSvgEvenWhenNamedPng()
    {
        await using MemoryStream source = new(
            "<svg xmlns='http://www.w3.org/2000/svg'></svg>"u8.ToArray());

        await Assert.ThrowsAsync<AssetValidationException>(
            () => _store.SaveAsync(
                source,
                "picture.png",
                CancellationToken.None));
    }

    [TestMethod]
    public async Task SaveAsync_RejectsTextNamedJpeg()
    {
        await using MemoryStream source = new("not an image"u8.ToArray());

        await Assert.ThrowsAsync<AssetValidationException>(
            () => _store.SaveAsync(
                source,
                "picture.jpg",
                CancellationToken.None));
    }

    [TestMethod]
    [DataRow(ImageFormat.Png)]
    [DataRow(ImageFormat.Jpeg)]
    public async Task SaveAsync_RejectsCorruptRasterData(ImageFormat format)
    {
        byte[] corrupt = format == ImageFormat.Png
            ? new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }
            : new byte[] { 255, 216, 255, 217 };
        await using MemoryStream source = new(corrupt);

        await Assert.ThrowsAsync<AssetValidationException>(
            () => _store.SaveAsync(
                source,
                $"picture.{GetExtension(format)}",
                CancellationToken.None));
    }

    [TestMethod]
    public async Task SaveAsync_AllowsValidImageAtFiveMiB()
    {
        await using MemoryStream source = await CreateImageAsync(
            width: 1,
            height: 1,
            ImageFormat.Png);
        source.SetLength(AssetStore.MaxSourceBytes);
        source.Position = 0;

        StoredAsset asset = await _store.SaveAsync(
            source,
            "boundary.png",
            CancellationToken.None);

        Assert.IsTrue(File.Exists(asset.FilePath));
    }

    [TestMethod]
    public async Task SaveAsync_RejectsInputOverFiveMiB()
    {
        await using MemoryStream source = new(
            new byte[AssetStore.MaxSourceBytes + 1],
            writable: false);

        await Assert.ThrowsAsync<AssetValidationException>(
            () => _store.SaveAsync(
                source,
                "oversize.jpg",
                CancellationToken.None));
    }

    [TestMethod]
    public async Task SaveAsync_AllowsImageAtMaximumDimensions()
    {
        await using MemoryStream source = await CreateImageAsync(
            width: AssetStore.MaxDimension,
            height: 1,
            ImageFormat.Png);

        StoredAsset asset = await _store.SaveAsync(
            source,
            "maximum.png",
            CancellationToken.None);

        Assert.IsTrue(File.Exists(asset.FilePath));
    }

    [TestMethod]
    public async Task SaveAsync_RejectsImageOverMaximumDimensions()
    {
        await using MemoryStream source = await CreateImageAsync(
            width: AssetStore.MaxDimension + 1,
            height: 1,
            ImageFormat.Png);

        await Assert.ThrowsAsync<AssetValidationException>(
            () => _store.SaveAsync(
                source,
                "too-wide.png",
                CancellationToken.None));
    }

    [TestMethod]
    public async Task SaveAsync_UsesRandomAppOwnedNameAndIgnoresTraversal()
    {
        await using MemoryStream firstSource = await CreateImageAsync(
            1,
            1,
            ImageFormat.Png);
        await using MemoryStream secondSource = await CreateImageAsync(
            1,
            1,
            ImageFormat.Png);

        StoredAsset first = await _store.SaveAsync(
            firstSource,
            "..\\..\\private\\same-name.png",
            CancellationToken.None);
        StoredAsset second = await _store.SaveAsync(
            secondSource,
            "..\\..\\private\\same-name.png",
            CancellationToken.None);

        Assert.AreNotEqual(first.AssetId, second.AssetId);
        Assert.AreNotEqual(first.FilePath, second.FilePath);
        Assert.IsTrue(Guid.TryParseExact(first.AssetId, "N", out _));
        Assert.AreEqual(
            first.AssetId,
            Path.GetFileNameWithoutExtension(first.FilePath));
        string expectedAssetsRoot = Path.GetFullPath(
            Path.Combine(_rootPath, "Assets"));
        Assert.AreEqual(
            expectedAssetsRoot,
            Path.GetDirectoryName(Path.GetFullPath(first.FilePath)));
        Assert.IsFalse(first.FilePath.Contains("private", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SaveAsync_DoesNotDeleteExistingAssets()
    {
        StoredAsset first = await SavePngAsync("first.png");

        _ = await SavePngAsync("second.png");

        Assert.IsTrue(File.Exists(first.FilePath));
    }

    [TestMethod]
    public async Task DeleteOrphansAfterCommitAsync_DeletesOnlyUnreferencedAssets()
    {
        StoredAsset referenced = await SavePngAsync("keep.png");
        StoredAsset orphan = await SavePngAsync("remove.png");
        IReadOnlySet<string> referencedIds = new HashSet<string>(
            StringComparer.Ordinal)
        {
            referenced.AssetId,
        };

        await _store.DeleteOrphansAfterCommitAsync(
            referencedIds,
            CancellationToken.None);

        Assert.IsTrue(File.Exists(referenced.FilePath));
        Assert.IsFalse(File.Exists(orphan.FilePath));
    }

    private async Task<StoredAsset> SavePngAsync(string originalFileName)
    {
        await using MemoryStream source = await CreateImageAsync(
            1,
            1,
            ImageFormat.Png);
        return await _store.SaveAsync(
            source,
            originalFileName,
            CancellationToken.None);
    }

    private static async Task<MemoryStream> CreateImageAsync(
        uint width,
        uint height,
        ImageFormat format)
    {
        using InMemoryRandomAccessStream stream = new();
        BitmapEncoder encoder = await BitmapEncoder.CreateAsync(
            GetEncoderId(format),
            stream);
        using SoftwareBitmap bitmap = new(
            BitmapPixelFormat.Bgra8,
            checked((int)width),
            checked((int)height),
            BitmapAlphaMode.Premultiplied);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        stream.Seek(0);
        using DataReader reader = new(stream.GetInputStreamAt(0));
        _ = await reader.LoadAsync(checked((uint)stream.Size));
        byte[] bytes = new byte[checked((int)stream.Size)];
        reader.ReadBytes(bytes);
        MemoryStream result = new(capacity: bytes.Length);
        await result.WriteAsync(bytes);
        result.Position = 0;
        return result;
    }

    private static async Task<InMemoryRandomAccessStream> ReadFileAsync(
        string filePath)
    {
        byte[] bytes = await File.ReadAllBytesAsync(filePath);
        InMemoryRandomAccessStream stream = new();
        using DataWriter writer = new(stream);
        writer.WriteBytes(bytes);
        _ = await writer.StoreAsync();
        writer.DetachStream();
        stream.Seek(0);
        return stream;
    }

    private static Guid GetEncoderId(ImageFormat format) => format switch
    {
        ImageFormat.Png => BitmapEncoder.PngEncoderId,
        ImageFormat.Jpeg => BitmapEncoder.JpegEncoderId,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static Guid GetDecoderId(ImageFormat format) => format switch
    {
        ImageFormat.Png => BitmapDecoder.PngDecoderId,
        ImageFormat.Jpeg => BitmapDecoder.JpegDecoderId,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static string GetExtension(ImageFormat format) => format switch
    {
        ImageFormat.Png => "png",
        ImageFormat.Jpeg => "jpg",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    private static string GetMediaType(ImageFormat format) => format switch
    {
        ImageFormat.Png => "image/png",
        ImageFormat.Jpeg => "image/jpeg",
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public enum ImageFormat
    {
        Png,
        Jpeg,
    }

    private sealed class TestAppDataPathProvider(string rootPath)
        : IAppDataPathProvider
    {
        public string DataRootPath { get; } = rootPath;
    }
}
