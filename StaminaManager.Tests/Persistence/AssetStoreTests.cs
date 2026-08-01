using StaminaManager.Infrastructure.Persistence;
using StaminaManager.Infrastructure.Storage;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace StaminaManager.Tests.Persistence;

[TestClass]
public sealed class AssetStoreTests
{
    private const string FixtureDirectoryName = "TestData";
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
        await using MemoryStream source = await OpenFixtureAsync(
            GetValidFixtureName(format));

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
        await using MemoryStream source = await OpenFixtureAsync("vector.svg");

        await Assert.ThrowsAsync<AssetValidationException>(
            () => _store.SaveAsync(
                source,
                "picture.png",
                CancellationToken.None));
    }

    [TestMethod]
    public async Task SaveAsync_RejectsTextNamedJpeg()
    {
        await using MemoryStream source = await OpenFixtureAsync(
            "text-disguised-as-jpeg.txt");

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
        await using MemoryStream source = await OpenFixtureAsync(
            $"corrupt.{GetExtension(format)}.base64");

        await Assert.ThrowsAsync<AssetValidationException>(
            () => _store.SaveAsync(
                source,
                $"picture.{GetExtension(format)}",
                CancellationToken.None));
    }

    [TestMethod]
    public async Task SaveAsync_AllowsValidImageOverFiveMiB()
    {
        await using MemoryStream source = await OpenFixtureAsync(
            GetValidFixtureName(ImageFormat.Png));
        source.SetLength(6 * 1024 * 1024);
        source.Position = 0;

        StoredAsset asset = await _store.SaveAsync(
            source,
            "large.png",
            CancellationToken.None);

        Assert.IsTrue(File.Exists(asset.FilePath));
    }

    [TestMethod]
    public async Task SaveAsync_LargeInvalidInputIsRejected()
    {
        await using MemoryStream source = new(
            new byte[6 * 1024 * 1024],
            writable: false);

        await Assert.ThrowsAsync<AssetValidationException>(
            () => _store.SaveAsync(
                source,
                "oversize.jpg",
                CancellationToken.None));
    }

    [TestMethod]
    public async Task SaveAsync_FailureRemovesInputTemporaryFile()
    {
        await using MemoryStream source = new(
            new byte[6 * 1024 * 1024],
            writable: false);

        await Assert.ThrowsAsync<AssetValidationException>(
            () => _store.SaveAsync(
                source,
                "invalid.png",
                CancellationToken.None));

        string assetsPath = Path.Combine(_rootPath, "Assets");
        Assert.IsFalse(
            Directory.Exists(assetsPath)
            && Directory.EnumerateFiles(
                assetsPath,
                "*.input.tmp",
                SearchOption.TopDirectoryOnly).Any());
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
        await using MemoryStream firstSource = await OpenFixtureAsync(
            GetValidFixtureName(ImageFormat.Png));
        await using MemoryStream secondSource = await OpenFixtureAsync(
            GetValidFixtureName(ImageFormat.Png));

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

    [TestMethod]
    public async Task SaveAsync_DestinationIOExceptionIsNotReclassified()
    {
        Directory.CreateDirectory(_rootPath);
        await File.WriteAllTextAsync(
            Path.Combine(_rootPath, "Assets"),
            "not a directory");
        await using MemoryStream source = await OpenFixtureAsync(
            GetValidFixtureName(ImageFormat.Png));

        await Assert.ThrowsExactlyAsync<IOException>(() => _store.SaveAsync(
            source,
            "valid.png",
            CancellationToken.None));
    }

    [TestMethod]
    public async Task DeleteOrphansAfterCommitAsync_DeletesOnlyOwnedTemps()
    {
        string assetsPath = Path.Combine(_rootPath, "Assets");
        Directory.CreateDirectory(assetsPath);
        string assetId = Guid.NewGuid().ToString("N");
        string ownedPngTemp = Path.Combine(assetsPath, $"{assetId}.png.tmp");
        string ownedJpegTemp = Path.Combine(assetsPath, $"{assetId}.jpg.tmp");
        string ownedInputTemp = Path.Combine(
            assetsPath,
            $"{assetId}.input.tmp");
        string arbitraryTemp = Path.Combine(assetsPath, "notes.tmp");
        string unsupportedTemp = Path.Combine(assetsPath, $"{assetId}.gif.tmp");
        await File.WriteAllTextAsync(ownedPngTemp, "partial");
        await File.WriteAllTextAsync(ownedJpegTemp, "partial");
        await File.WriteAllTextAsync(ownedInputTemp, "partial");
        await File.WriteAllTextAsync(arbitraryTemp, "keep");
        await File.WriteAllTextAsync(unsupportedTemp, "keep");

        await _store.DeleteOrphansAfterCommitAsync(
            new HashSet<string>(StringComparer.Ordinal),
            CancellationToken.None);

        Assert.IsFalse(File.Exists(ownedPngTemp));
        Assert.IsFalse(File.Exists(ownedJpegTemp));
        Assert.IsFalse(File.Exists(ownedInputTemp));
        Assert.IsTrue(File.Exists(arbitraryTemp));
        Assert.IsTrue(File.Exists(unsupportedTemp));
    }

    [TestMethod]
    public async Task DeleteOrphansAfterCommitAsync_WaitsForSharedRootSave()
    {
        AssetStore secondStore = new(
            new TestAppDataPathProvider(_rootPath));
        MemoryStream fixture = await OpenFixtureAsync(
            GetValidFixtureName(ImageFormat.Png));
        await using PausingReadStream source = new(fixture);
        Task<StoredAsset> saveTask = _store.SaveAsync(
            source,
            "concurrent.png",
            CancellationToken.None);
        await source.ReadStarted;
        string assetsPath = Path.Combine(_rootPath, "Assets");
        Directory.CreateDirectory(assetsPath);
        string temporaryPath = Path.Combine(
            assetsPath,
            $"{Guid.NewGuid():N}.png.tmp");
        await File.WriteAllTextAsync(temporaryPath, "in progress");

        Task cleanupTask = secondStore.DeleteOrphansAfterCommitAsync(
            new HashSet<string>(StringComparer.Ordinal),
            CancellationToken.None);
        try
        {
            Assert.IsFalse(
                cleanupTask.IsCompleted,
                "Cleanup must wait while a save owns the shared asset gate.");
            Assert.IsTrue(File.Exists(temporaryPath));
        }
        finally
        {
            source.Resume();
            _ = await saveTask;
            await cleanupTask;
        }

        Assert.IsFalse(File.Exists(temporaryPath));
    }

    private async Task<StoredAsset> SavePngAsync(string originalFileName)
    {
        await using MemoryStream source = await OpenFixtureAsync(
            GetValidFixtureName(ImageFormat.Png));
        return await _store.SaveAsync(
            source,
            originalFileName,
            CancellationToken.None);
    }

    private static async Task<MemoryStream> OpenFixtureAsync(
        string fixtureFileName)
    {
        string path = Path.Combine(
            AppContext.BaseDirectory,
            FixtureDirectoryName,
            "Images",
            fixtureFileName);
        byte[] bytes;
        if (path.EndsWith(".base64", StringComparison.OrdinalIgnoreCase))
        {
            string encoded = await File.ReadAllTextAsync(path);
            bytes = Convert.FromBase64String(encoded);
        }
        else
        {
            bytes = await File.ReadAllBytesAsync(path);
        }

        MemoryStream result = new(capacity: bytes.Length);
        await result.WriteAsync(bytes);
        result.Position = 0;
        return result;
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

    private static string GetValidFixtureName(ImageFormat format) =>
        format switch
        {
            ImageFormat.Png => "valid-1x1.png.base64",
            ImageFormat.Jpeg => "valid-1x1.jpg.base64",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };

    public enum ImageFormat
    {
        Png,
        Jpeg,
    }

    private sealed class PausingReadStream(Stream inner) : Stream
    {
        private readonly TaskCompletionSource<bool> _readStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _resume = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task ReadStarted => _readStarted.Task;

        public override bool CanRead => inner.CanRead;

        public override bool CanSeek => inner.CanSeek;

        public override bool CanWrite => false;

        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public void Resume() => _resume.TrySetResult(true);

        public override void Flush() => inner.Flush();

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            _readStarted.TrySetResult(true);
            await _resume.Task.WaitAsync(cancellationToken);
            return await inner.ReadAsync(buffer, cancellationToken);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            inner.Seek(offset, origin);

        public override void SetLength(long value) =>
            throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class TestAppDataPathProvider(string rootPath)
        : IAppDataPathProvider
    {
        public string DataRootPath { get; } = rootPath;
    }
}
