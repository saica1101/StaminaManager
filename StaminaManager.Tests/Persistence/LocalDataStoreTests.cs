using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Persistence;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;

namespace StaminaManager.Tests.Persistence;

[TestClass]
public sealed class LocalDataStoreTests
{
    private string _rootPath = null!;
    private LocalDataStore _store = null!;

    [TestInitialize]
    public void Initialize()
    {
        _rootPath = Path.Combine(
            Path.GetTempPath(),
            $"StaminaManagerTests-{Guid.NewGuid():N}");
        _store = new LocalDataStore(new TestAppDataPathProvider(_rootPath));
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
    public async Task SaveAndLoadAsync_RoundTripsSchemaGamesAndSettings()
    {
        DataEnvelope expected = CreateEnvelope(
            "First game",
            AppTheme.Dark,
            new DateTimeOffset(2026, 7, 25, 12, 30, 0, TimeSpan.Zero));

        await _store.SaveAsync(expected, CancellationToken.None);
        DataLoadResult result = await _store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Primary, result.Status);
        Assert.IsNotNull(result.Envelope);
        Assert.AreEqual(
            DataEnvelope.CurrentSchemaVersion,
            result.Envelope.SchemaVersion);
        Assert.HasCount(1, result.Envelope.Games);
        Assert.AreEqual(expected.Games[0], result.Envelope.Games[0]);
        Assert.AreEqual(expected.Settings, result.Envelope.Settings);
    }

    [TestMethod]
    public async Task SaveAsync_WritesStableCamelCaseJsonWithStringEnumsAndUtc()
    {
        DataEnvelope envelope = CreateEnvelope(
            "Offset game",
            AppTheme.Dark,
            new DateTimeOffset(2026, 7, 25, 21, 30, 0, TimeSpan.FromHours(9)));

        await _store.SaveAsync(envelope, CancellationToken.None);

        string json = await File.ReadAllTextAsync(PrimaryPath);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        Assert.AreEqual(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(
            "Offset game",
            root.GetProperty("games")[0].GetProperty("name").GetString());
        Assert.AreEqual(
            "2026-07-25T12:30:00+00:00",
            root.GetProperty("games")[0]
                .GetProperty("recordedAtUtc")
                .GetString());
        JsonElement settings = root.GetProperty("settings");
        Assert.AreEqual("Dark", settings.GetProperty("theme").GetString());
        Assert.AreEqual(
            "Mica",
            settings.GetProperty("backdrop").GetString());
        Assert.AreEqual(
            "MinimizeToTray",
            settings.GetProperty("closeBehavior").GetString());
        Assert.IsFalse(json.Contains("SchemaVersion", StringComparison.Ordinal));
        Assert.IsLessThan(
            json.IndexOf("\"games\"", StringComparison.Ordinal),
            json.IndexOf("\"schemaVersion\"", StringComparison.Ordinal));
        Assert.IsLessThan(
            json.IndexOf("\"settings\"", StringComparison.Ordinal),
            json.IndexOf("\"games\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task SaveAsync_FirstSaveCreatesPrimaryWithoutTemporaryFile()
    {
        await _store.SaveAsync(
            CreateEnvelope("First", AppTheme.Light),
            CancellationToken.None);

        Assert.IsTrue(File.Exists(PrimaryPath));
        Assert.IsFalse(File.Exists(TemporaryPath));
        Assert.IsFalse(File.Exists(RecoveryPath));
    }

    [TestMethod]
    public async Task SaveAsync_SecondSaveKeepsPreviousPrimaryAsRecovery()
    {
        await _store.SaveAsync(
            CreateEnvelope("Previous", AppTheme.Light),
            CancellationToken.None);
        byte[] previousBytes = await File.ReadAllBytesAsync(PrimaryPath);

        await _store.SaveAsync(
            CreateEnvelope("Current", AppTheme.Dark),
            CancellationToken.None);

        byte[] recoveryBytes = await File.ReadAllBytesAsync(RecoveryPath);
        CollectionAssert.AreEqual(previousBytes, recoveryBytes);
        DataLoadResult loaded = await _store.LoadAsync(
            CancellationToken.None);
        Assert.AreEqual(DataLoadStatus.Primary, loaded.Status);
        Assert.AreEqual("Current", loaded.Envelope!.Games[0].Name);
        Assert.IsFalse(File.Exists(TemporaryPath));
    }

    [TestMethod]
    public async Task LoadAsync_CorruptPrimaryReturnsRecoveryWithoutMutation()
    {
        await _store.SaveAsync(
            CreateEnvelope("Recovery", AppTheme.Light),
            CancellationToken.None);
        await _store.SaveAsync(
            CreateEnvelope("Broken later", AppTheme.Dark),
            CancellationToken.None);
        byte[] corruptPrimary = Encoding.UTF8.GetBytes("{not valid json");
        await File.WriteAllBytesAsync(PrimaryPath, corruptPrimary);
        byte[] recoveryBefore = await File.ReadAllBytesAsync(RecoveryPath);

        DataLoadResult result = await _store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Recovery, result.Status);
        Assert.AreEqual("Recovery", result.Envelope!.Games[0].Name);
        Assert.AreEqual(PrimaryPath, result.PrimaryPath);
        Assert.AreEqual(RecoveryPath, result.RecoveryPath);
        CollectionAssert.AreEqual(
            corruptPrimary,
            await File.ReadAllBytesAsync(PrimaryPath));
        CollectionAssert.AreEqual(
            recoveryBefore,
            await File.ReadAllBytesAsync(RecoveryPath));
    }

    [TestMethod]
    public async Task LoadAsync_BothFilesCorruptReturnsCorruptAndPreservesFiles()
    {
        Directory.CreateDirectory(_rootPath);
        byte[] corruptPrimary = Encoding.UTF8.GetBytes("primary-corrupt");
        byte[] corruptRecovery = Encoding.UTF8.GetBytes("recovery-corrupt");
        await File.WriteAllBytesAsync(PrimaryPath, corruptPrimary);
        await File.WriteAllBytesAsync(RecoveryPath, corruptRecovery);

        DataLoadResult result = await _store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Corrupt, result.Status);
        Assert.IsNull(result.Envelope);
        Assert.AreEqual(PrimaryPath, result.PrimaryPath);
        Assert.AreEqual(RecoveryPath, result.RecoveryPath);
        CollectionAssert.AreEqual(
            corruptPrimary,
            await File.ReadAllBytesAsync(PrimaryPath));
        CollectionAssert.AreEqual(
            corruptRecovery,
            await File.ReadAllBytesAsync(RecoveryPath));
    }

    [TestMethod]
    public async Task LoadAsync_NoFilesReturnsEmpty()
    {
        DataLoadResult result = await _store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Empty, result.Status);
        Assert.IsNull(result.Envelope);
        Assert.AreEqual(PrimaryPath, result.PrimaryPath);
        Assert.AreEqual(RecoveryPath, result.RecoveryPath);
    }

    [TestMethod]
    public async Task LoadAsync_UnknownSchemaReturnsCorruptAndPreservesFile()
    {
        await _store.SaveAsync(
            CreateEnvelope("Unknown", AppTheme.Light),
            CancellationToken.None);
        string currentJson = await File.ReadAllTextAsync(PrimaryPath);
        byte[] unsupported = Encoding.UTF8.GetBytes(currentJson.Replace(
            "\"schemaVersion\":1",
            "\"schemaVersion\":99",
            StringComparison.Ordinal));
        await File.WriteAllBytesAsync(PrimaryPath, unsupported);

        DataLoadResult result = await _store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Corrupt, result.Status);
        Assert.IsNull(result.Envelope);
        CollectionAssert.AreEqual(
            unsupported,
            await File.ReadAllBytesAsync(PrimaryPath));
    }

    [TestMethod]
    public async Task SaveAsync_CorruptPrimaryIsNotOverwritten()
    {
        Directory.CreateDirectory(_rootPath);
        byte[] corruptPrimary = Encoding.UTF8.GetBytes("do-not-overwrite");
        await File.WriteAllBytesAsync(PrimaryPath, corruptPrimary);

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.SaveAsync(
            CreateEnvelope("New", AppTheme.Dark),
            CancellationToken.None));

        CollectionAssert.AreEqual(
            corruptPrimary,
            await File.ReadAllBytesAsync(PrimaryPath));
        Assert.IsFalse(File.Exists(TemporaryPath));
    }

    private string PrimaryPath => Path.Combine(_rootPath, "data.json");

    private string RecoveryPath =>
        Path.Combine(_rootPath, "data.recovery.json");

    private string TemporaryPath =>
        Path.Combine(_rootPath, "data.json.tmp");

    private static DataEnvelope CreateEnvelope(
        string gameName,
        AppTheme theme,
        DateTimeOffset? recordedAt = null)
    {
        GameEntry game = new(
            Guid.Parse("188cb17c-8e2d-469b-b00f-d682af25f0fe"),
            gameName,
            BaseStamina: 40,
            MaxStamina: 100,
            RecoveryMinutes: 5,
            recordedAt ?? new DateTimeOffset(
                2026,
                7,
                25,
                12,
                30,
                0,
                TimeSpan.Zero),
            ImageAssetId: "asset-id",
            SortOrder: 0);
        return new DataEnvelope(
            DataEnvelope.CurrentSchemaVersion,
            ImmutableArray.Create(game),
            AppSettings.CreateDefault(theme));
    }

    private sealed class TestAppDataPathProvider(string rootPath)
        : IAppDataPathProvider
    {
        public string DataRootPath { get; } = rootPath;
    }
}
