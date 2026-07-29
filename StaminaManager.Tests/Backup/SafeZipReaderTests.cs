using StaminaManager.Infrastructure.Backup;
using System.IO.Compression;
using System.Text;

namespace StaminaManager.Tests.Backup;

[TestClass]
public sealed class SafeZipReaderTests
{
    [TestMethod]
    [DataRow("/absolute.json")]
    [DataRow("C:/absolute.json")]
    [DataRow("../data.json")]
    [DataRow("assets/../../data.json")]
    public async Task ReadAsync_危険なentryPathを拒否する(string path)
    {
        await using MemoryStream archive = CreateArchive(
            ("manifest.json", ValidManifest),
            ("data.json", ValidData),
            (path, "x"));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_大文字小文字違いの重複entryを拒否する()
    {
        await using MemoryStream archive = CreateArchive(
            ("manifest.json", ValidManifest),
            ("MANIFEST.JSON", ValidManifest),
            ("data.json", ValidData));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_200文字を超えるpathを拒否する()
    {
        string path = $"assets/{new string('a', 190)}.png";
        await using MemoryStream archive = CreateArchive(
            ("manifest.json", ValidManifest),
            ("data.json", ValidData),
            (path, "x"));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_202件を超えるentryを拒否する()
    {
        List<(string Name, string Content)> entries =
        [
            ("manifest.json", ValidManifest),
            ("data.json", ValidData),
        ];
        for (int index = 0; index < 201; index++)
        {
            entries.Add(($"extra/{index}.txt", string.Empty));
        }

        await using MemoryStream archive = CreateArchive([.. entries]);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_圧縮比100を超えるentryを拒否する()
    {
        string oversizedRatio = new('a', 64 * 1024);
        await using MemoryStream archive = CreateArchive(
            ("manifest.json", ValidManifest),
            ("data.json", ValidData),
            ("extra.txt", oversizedRatio));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_未知のdataSchemaを拒否する()
    {
        const string manifest =
            """
            {"schemaVersion":1,"dataSchemaVersion":999,"dataFile":"data.json","assets":[]}
            """;
        await using MemoryStream archive = CreateArchive(
            ("manifest.json", manifest),
            ("data.json", ValidData));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_256MiBではなく256KiBを超えるmanifestを拒否する()
    {
        string manifest = new(' ', 256 * 1024 + 1);
        await using MemoryStream archive = CreateArchive(
            CompressionLevel.NoCompression,
            ("manifest.json", manifest),
            ("data.json", ValidData));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_4MiBを超えるdataJsonを拒否する()
    {
        string data = new(' ', 4 * 1024 * 1024 + 1);
        await using MemoryStream archive = CreateArchive(
            CompressionLevel.NoCompression,
            ("manifest.json", ValidManifest),
            ("data.json", data));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_5MiBを超える画像を拒否する()
    {
        string assetId = Guid.NewGuid().ToString("N");
        string manifest = CreateManifest(assetId);
        string data = CreateDataWithGames(1, assetId);
        string image = new('x', 5 * 1024 * 1024 + 1);
        await using MemoryStream archive = CreateArchive(
            CompressionLevel.NoCompression,
            ("manifest.json", manifest),
            ("data.json", data),
            ($"assets/{assetId}.png", image));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_decodeできない画像を拒否する()
    {
        string assetId = Guid.NewGuid().ToString("N");
        await using MemoryStream archive = CreateArchive(
            CompressionLevel.NoCompression,
            ("manifest.json", CreateManifest(assetId)),
            ("data.json", CreateDataWithGames(1, assetId)),
            ($"assets/{assetId}.png", "not an image"));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_ゲーム101件を拒否する()
    {
        await using MemoryStream archive = CreateArchive(
            CompressionLevel.NoCompression,
            ("manifest.json", ValidManifest),
            ("data.json", CreateDataWithGames(101, imageAssetId: null)));

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_展開後合計512MiB超をmetadataだけで拒否する()
    {
        await using MemoryStream archive = CreateArchive(
            CompressionLevel.NoCompression,
            ("manifest.json", ValidManifest),
            ("data.json", ValidData));
        PatchCentralDirectorySizes(
            archive,
            "manifest.json",
            compressedSize: 4 * 1024 * 1024,
            expandedSize: 300 * 1024 * 1024);
        PatchCentralDirectorySizes(
            archive,
            "data.json",
            compressedSize: 4 * 1024 * 1024,
            expandedSize: 300 * 1024 * 1024);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_圧縮0かつ展開後非0をmetadataだけで拒否する()
    {
        await using MemoryStream archive = CreateArchive(
            CompressionLevel.NoCompression,
            ("manifest.json", ValidManifest),
            ("data.json", ValidData));
        PatchCentralDirectorySizes(
            archive,
            "data.json",
            compressedSize: 0,
            expandedSize: 1);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            () => new SafeZipReader().ReadAsync(
                archive,
                CancellationToken.None));
    }

    [TestMethod]
    public async Task ReadAsync_正常なbackupからpreviewを返す()
    {
        await using MemoryStream archive = CreateArchive(
            ("manifest.json", ValidManifest),
            ("data.json", ValidData));

        ValidatedBackup backup = await new SafeZipReader().ReadAsync(
            archive,
            CancellationToken.None);

        Assert.AreEqual(0, backup.Preview.GameCount);
        Assert.AreEqual(0, backup.Preview.ImageCount);
        Assert.AreEqual("Light", backup.Preview.Theme);
    }

    [TestMethod]
    public async Task ReadToStageAsync_画像100件でもmanagedBufferを累積保持しない()
    {
        List<int> retainedBytes = [];
        string root = Path.Combine(
            Path.GetTempPath(),
            "StaminaManager.StreamingBackupTests",
            Guid.NewGuid().ToString("N"));
        string assets = Path.Combine(root, "Assets");
        Directory.CreateDirectory(assets);
        try
        {
            await using MemoryStream archive =
                await CreateImageArchiveAsync(assetCount: 100);

            ValidatedBackup backup = await new SafeZipReader(
                retainedBytes.Add).ReadToStageAsync(
                    archive,
                    assets,
                    CancellationToken.None);

            Assert.HasCount(100, backup.Assets);
            Assert.AreEqual(100, Directory.EnumerateFiles(assets).Count());
            Assert.IsLessThanOrEqualTo(
                4 * 1024 * 1024,
                retainedBytes.Max());
            Assert.AreEqual(0, retainedBytes[^1]);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static MemoryStream CreateArchive(
        params (string Name, string Content)[] entries) =>
        CreateArchive(CompressionLevel.Optimal, entries);

    private static MemoryStream CreateArchive(
        CompressionLevel compressionLevel,
        params (string Name, string Content)[] entries)
    {
        MemoryStream stream = new();
        using (ZipArchive archive = new(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            foreach ((string name, string content) in entries)
            {
                ZipArchiveEntry entry = archive.CreateEntry(
                    name,
                    compressionLevel);
                using StreamWriter writer = new(
                    entry.Open(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static string CreateManifest(string assetId) =>
        $$"""
        {"schemaVersion":1,"dataSchemaVersion":1,"dataFile":"data.json","assets":[{"assetId":"{{assetId}}","entryPath":"assets/{{assetId}}.png","mediaType":"image/png"}]}
        """;

    private static async Task<MemoryStream> CreateImageArchiveAsync(
        int assetCount)
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "TestData",
            "Images",
            "valid-1x1.png.base64");
        byte[] image = Convert.FromBase64String(
            await File.ReadAllTextAsync(fixturePath));
        string[] assetIds = Enumerable.Range(0, assetCount)
            .Select(_ => Guid.NewGuid().ToString("N"))
            .ToArray();
        string manifest = System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            dataSchemaVersion = 1,
            dataFile = "data.json",
            assets = assetIds.Select(assetId => new
            {
                assetId,
                entryPath = $"assets/{assetId}.png",
                mediaType = "image/png",
            }),
        });
        string data = CreateDataWithAssetIds(assetIds);
        MemoryStream stream = new();
        using (ZipArchive archive = new(
            stream,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            WriteTextEntry(archive, "manifest.json", manifest);
            WriteTextEntry(archive, "data.json", data);
            foreach (string assetId in assetIds)
            {
                ZipArchiveEntry entry = archive.CreateEntry(
                    $"assets/{assetId}.png",
                    CompressionLevel.NoCompression);
                await using Stream output = entry.Open();
                await output.WriteAsync(image);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteTextEntry(
        ZipArchive archive,
        string name,
        string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(
            name,
            CompressionLevel.NoCompression);
        using StreamWriter writer = new(
            entry.Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string CreateDataWithAssetIds(
        IReadOnlyList<string> assetIds) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            games = assetIds.Select((assetId, index) => new
            {
                id = Guid.NewGuid(),
                name = $"game{index}",
                baseStamina = 1,
                maxStamina = 100,
                recoveryMinutes = 5,
                recoverySeconds = 0,
                recordedAtUtc = "2026-01-01T00:00:00+00:00",
                imageAssetId = assetId,
                sortOrder = index,
                isNotificationEnabled = true,
            }),
            settings = new
            {
                theme = "Light",
                backdrop = "Mica",
                notificationsEnabled = true,
                notificationLeadMinutes = 15,
                closeBehavior = "MinimizeToTray",
                startupEnabled = false,
                lastDisplayMode = "Standard",
                selectedCompactGameId = (Guid?)null,
            },
        });

    private static void PatchCentralDirectorySizes(
        MemoryStream archive,
        string entryName,
        uint compressedSize,
        uint expandedSize)
    {
        const uint centralDirectorySignature = 0x02014b50;
        byte[] bytes = archive.GetBuffer();
        int length = checked((int)archive.Length);
        for (int offset = 0; offset <= length - 46; offset++)
        {
            if (BitConverter.ToUInt32(bytes, offset)
                    != centralDirectorySignature)
            {
                continue;
            }

            ushort nameLength = BitConverter.ToUInt16(bytes, offset + 28);
            string name = Encoding.UTF8.GetString(
                bytes,
                offset + 46,
                nameLength);
            if (!string.Equals(name, entryName, StringComparison.Ordinal))
            {
                continue;
            }

            BitConverter.GetBytes(compressedSize).CopyTo(bytes, offset + 20);
            BitConverter.GetBytes(expandedSize).CopyTo(bytes, offset + 24);
            archive.Position = 0;
            return;
        }

        Assert.Fail($"Central directory entry not found: {entryName}");
    }

    private static string CreateDataWithGames(
        int gameCount,
        string? imageAssetId)
    {
        object[] games = Enumerable.Range(0, gameCount)
            .Select(index => (object)new
            {
                id = Guid.NewGuid(),
                name = $"game{index}",
                baseStamina = 1,
                maxStamina = 100,
                recoveryMinutes = 5,
                recoverySeconds = 0,
                recordedAtUtc = "2026-01-01T00:00:00+00:00",
                imageAssetId,
                sortOrder = index,
                isNotificationEnabled = true,
            })
            .ToArray();
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            games,
            settings = new
            {
                theme = "Light",
                backdrop = "Mica",
                notificationsEnabled = true,
                notificationLeadMinutes = 15,
                closeBehavior = "MinimizeToTray",
                startupEnabled = false,
                lastDisplayMode = "Standard",
                selectedCompactGameId = (Guid?)null,
            },
        });
    }

    private const string ValidManifest =
        """
        {"schemaVersion":1,"dataSchemaVersion":1,"dataFile":"data.json","assets":[]}
        """;

    private const string ValidData =
        """
        {"schemaVersion":1,"games":[],"settings":{"theme":"Light","backdrop":"Mica","notificationsEnabled":true,"notificationLeadMinutes":15,"closeBehavior":"MinimizeToTray","startupEnabled":false,"lastDisplayMode":"Standard","selectedCompactGameId":null}}
        """;
}
