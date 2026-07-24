using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Persistence;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

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

    [TestMethod]
    public async Task SaveAsync_RemovesOnlyExactStaleTemporaryFile()
    {
        Directory.CreateDirectory(_rootPath);
        await File.WriteAllTextAsync(TemporaryPath, "stale");
        string unrelatedPath = $"{TemporaryPath}.keep";
        await File.WriteAllTextAsync(unrelatedPath, "keep");

        await _store.SaveAsync(
            CreateEnvelope("Recovered save", AppTheme.Light),
            CancellationToken.None);

        Assert.IsTrue(File.Exists(PrimaryPath));
        Assert.IsFalse(File.Exists(TemporaryPath));
        Assert.IsTrue(File.Exists(unrelatedPath));
    }

    [TestMethod]
    public async Task SaveAsync_ConcurrentCallsAreSerialized()
    {
        LocalDataStore secondStore = new(
            new TestAppDataPathProvider(_rootPath));
        Task[] saves = Enumerable.Range(0, 8)
            .Select(index => (index % 2 == 0 ? _store : secondStore).SaveAsync(
                CreateEnvelope($"Game {index}", AppTheme.Light),
                CancellationToken.None))
            .ToArray();

        await Task.WhenAll(saves);

        DataLoadResult result = await _store.LoadAsync(
            CancellationToken.None);
        Assert.AreEqual(DataLoadStatus.Primary, result.Status);
        Assert.IsFalse(File.Exists(TemporaryPath));
    }

    [TestMethod]
    public async Task LoadAsync_JsonOverFourMiBReturnsCorrupt()
    {
        await _store.SaveAsync(
            CreateEnvelope("Size limit", AppTheme.Light),
            CancellationToken.None);
        byte[] json = await File.ReadAllBytesAsync(PrimaryPath);
        int paddingLength = FourMiB + 1 - json.Length;
        byte[] padding = new byte[paddingLength];
        Array.Fill(padding, (byte)' ');
        await using (FileStream stream = new(
            PrimaryPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None))
        {
            await stream.WriteAsync(padding);
        }

        DataLoadResult result = await _store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Corrupt, result.Status);
        Assert.IsNull(result.Envelope);
    }

    [TestMethod]
    public async Task LoadAsync_AllowsConcurrentAtomicSave()
    {
        await _store.SaveAsync(
            CreateEnvelope("Before", AppTheme.Light),
            CancellationToken.None);
        int paddingLength = checked(
            (int)(FourMiB - new FileInfo(PrimaryPath).Length));
        await using (FileStream stream = new(
            PrimaryPath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None))
        {
            byte[] padding = new byte[paddingLength];
            Array.Fill(padding, (byte)' ');
            await stream.WriteAsync(padding);
        }

        Task<DataLoadResult> loadTask = _store.LoadAsync(
            CancellationToken.None);
        await _store.SaveAsync(
            CreateEnvelope("After", AppTheme.Dark),
            CancellationToken.None);

        DataLoadResult concurrentLoad = await loadTask;
        DataLoadResult saved = await _store.LoadAsync(CancellationToken.None);
        Assert.AreEqual(DataLoadStatus.Primary, concurrentLoad.Status);
        Assert.AreEqual("After", saved.Envelope!.Games[0].Name);
    }

    [TestMethod]
    public async Task SaveAsync_JsonOverFourMiBIsRejected()
    {
        DataEnvelope envelope = CreateEnvelope(
            new string('x', FourMiB),
            AppTheme.Light);

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.SaveAsync(
            envelope,
            CancellationToken.None));

        Assert.IsFalse(File.Exists(PrimaryPath));
        Assert.IsFalse(File.Exists(TemporaryPath));
    }

    [TestMethod]
    [DataRow(InvalidEnvelopeKind.TooManyGames)]
    [DataRow(InvalidEnvelopeKind.DuplicateGameId)]
    [DataRow(InvalidEnvelopeKind.InvalidSortOrder)]
    [DataRow(InvalidEnvelopeKind.InvalidAssetId)]
    [DataRow(InvalidEnvelopeKind.InvalidNotificationLead)]
    [DataRow(InvalidEnvelopeKind.InvalidTheme)]
    [DataRow(InvalidEnvelopeKind.InvalidBackdrop)]
    [DataRow(InvalidEnvelopeKind.InvalidCloseBehavior)]
    [DataRow(InvalidEnvelopeKind.BlankName)]
    [DataRow(InvalidEnvelopeKind.InvalidBaseStamina)]
    [DataRow(InvalidEnvelopeKind.InvalidMaxStamina)]
    [DataRow(InvalidEnvelopeKind.InvalidRecoveryMinutes)]
    [DataRow(InvalidEnvelopeKind.InvalidFullTime)]
    [DataRow(InvalidEnvelopeKind.EmptyGameId)]
    [DataRow(InvalidEnvelopeKind.MissingSettings)]
    public async Task SaveAsync_SemanticallyInvalidEnvelopeIsRejected(
        InvalidEnvelopeKind kind)
    {
        DataEnvelope envelope = CreateInvalidEnvelope(kind);

        await Assert.ThrowsAsync<InvalidDataException>(() => _store.SaveAsync(
            envelope,
            CancellationToken.None));

        Assert.IsFalse(File.Exists(PrimaryPath));
    }

    [TestMethod]
    public async Task LoadAsync_SemanticallyInvalidEnvelopeReturnsCorrupt()
    {
        await _store.SaveAsync(
            CreateEnvelope("Valid name", AppTheme.Light),
            CancellationToken.None);
        string json = await File.ReadAllTextAsync(PrimaryPath);
        json = json.Replace(
            "\"name\":\"Valid name\"",
            "\"name\":\" \"",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(PrimaryPath, json);

        DataLoadResult result = await _store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Corrupt, result.Status);
    }

    [TestMethod]
    public async Task LoadAsync_MissingRequiredSettingReturnsCorrupt()
    {
        JsonObject root = await SaveAndReadJsonObjectAsync();
        _ = root["settings"]!.AsObject().Remove("notificationsEnabled");
        await File.WriteAllTextAsync(PrimaryPath, root.ToJsonString());

        DataLoadResult result = await _store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Corrupt, result.Status);
    }

    [TestMethod]
    public async Task LoadAsync_NumericEnumTokenReturnsCorrupt()
    {
        JsonObject root = await SaveAndReadJsonObjectAsync();
        root["settings"]!["theme"] = 0;
        await File.WriteAllTextAsync(PrimaryPath, root.ToJsonString());

        DataLoadResult result = await _store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Corrupt, result.Status);
    }

    [TestMethod]
    public async Task LoadAsync_MissingPrimaryWithValidRecoveryReturnsRecovery()
    {
        await CreateRecoveryAsync();
        File.Delete(PrimaryPath);

        DataLoadResult result = await _store.LoadAsync(
            CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Recovery, result.Status);
        Assert.AreEqual("Recovery", result.Envelope!.Games[0].Name);
    }

    [TestMethod]
    public async Task PromoteRecoveryAsync_PreservesCorruptPrimaryAsDiagnostic()
    {
        await CreateRecoveryAsync();
        byte[] corruptPrimary = Encoding.UTF8.GetBytes("corrupt primary");
        await File.WriteAllBytesAsync(PrimaryPath, corruptPrimary);
        byte[] recoveryBefore = await File.ReadAllBytesAsync(RecoveryPath);

        RecoveryPromotionResult promotion =
            await _store.PromoteRecoveryAsync(CancellationToken.None);

        Assert.IsNotNull(promotion.DiagnosticBackupPath);
        Assert.IsTrue(File.Exists(promotion.DiagnosticBackupPath));
        CollectionAssert.AreEqual(
            corruptPrimary,
            await File.ReadAllBytesAsync(promotion.DiagnosticBackupPath));
        CollectionAssert.AreEqual(
            recoveryBefore,
            await File.ReadAllBytesAsync(RecoveryPath));
        DataLoadResult loaded = await _store.LoadAsync(
            CancellationToken.None);
        Assert.AreEqual(DataLoadStatus.Primary, loaded.Status);
        Assert.AreEqual("Recovery", loaded.Envelope!.Games[0].Name);
    }

    [TestMethod]
    public async Task PromoteRecoveryAsync_MissingPrimaryUsesAtomicMove()
    {
        await CreateRecoveryAsync();
        File.Delete(PrimaryPath);

        RecoveryPromotionResult promotion =
            await _store.PromoteRecoveryAsync(CancellationToken.None);

        Assert.IsNull(promotion.DiagnosticBackupPath);
        Assert.IsTrue(File.Exists(PrimaryPath));
        Assert.IsFalse(File.Exists(TemporaryPath));
        DataLoadResult loaded = await _store.LoadAsync(
            CancellationToken.None);
        Assert.AreEqual(DataLoadStatus.Primary, loaded.Status);
    }

    [TestMethod]
    public async Task PromoteRecoveryAsync_InvalidRecoveryPreservesPrimary()
    {
        Directory.CreateDirectory(_rootPath);
        byte[] primary = Encoding.UTF8.GetBytes("primary diagnostics");
        byte[] recovery = Encoding.UTF8.GetBytes("invalid recovery");
        await File.WriteAllBytesAsync(PrimaryPath, primary);
        await File.WriteAllBytesAsync(RecoveryPath, recovery);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => _store.PromoteRecoveryAsync(CancellationToken.None));

        CollectionAssert.AreEqual(
            primary,
            await File.ReadAllBytesAsync(PrimaryPath));
        CollectionAssert.AreEqual(
            recovery,
            await File.ReadAllBytesAsync(RecoveryPath));
        Assert.IsFalse(File.Exists(TemporaryPath));
    }

    private string PrimaryPath => Path.Combine(_rootPath, "data.json");

    private string RecoveryPath =>
        Path.Combine(_rootPath, "data.recovery.json");

    private string TemporaryPath =>
        Path.Combine(_rootPath, "data.json.tmp");

    private const int FourMiB = 4 * 1024 * 1024;

    private async Task<JsonObject> SaveAndReadJsonObjectAsync()
    {
        await _store.SaveAsync(
            CreateEnvelope("Required", AppTheme.Light),
            CancellationToken.None);
        string json = await File.ReadAllTextAsync(PrimaryPath);
        return JsonNode.Parse(json)!.AsObject();
    }

    private async Task CreateRecoveryAsync()
    {
        await _store.SaveAsync(
            CreateEnvelope("Recovery", AppTheme.Light),
            CancellationToken.None);
        await _store.SaveAsync(
            CreateEnvelope("Primary", AppTheme.Dark),
            CancellationToken.None);
    }

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
            ImageAssetId: "0123456789abcdef0123456789abcdef",
            SortOrder: 0);
        return new DataEnvelope(
            DataEnvelope.CurrentSchemaVersion,
            ImmutableArray.Create(game),
            AppSettings.CreateDefault(theme));
    }

    private static DataEnvelope CreateInvalidEnvelope(
        InvalidEnvelopeKind kind)
    {
        DataEnvelope envelope = CreateEnvelope("Valid", AppTheme.Light);
        GameEntry game = envelope.Games[0];
        return kind switch
        {
            InvalidEnvelopeKind.TooManyGames => envelope with
            {
                Games = Enumerable.Range(0, 101)
                    .Select(index => game with
                    {
                        Id = Guid.NewGuid(),
                        ImageAssetId = null,
                        SortOrder = index,
                    })
                    .ToImmutableArray(),
            },
            InvalidEnvelopeKind.DuplicateGameId => envelope with
            {
                Games = ImmutableArray.Create(
                    game,
                    game with { SortOrder = 1 }),
            },
            InvalidEnvelopeKind.InvalidSortOrder => envelope with
            {
                Games = ImmutableArray.Create(game with { SortOrder = 1 }),
            },
            InvalidEnvelopeKind.InvalidAssetId => envelope with
            {
                Games = ImmutableArray.Create(game with
                {
                    ImageAssetId = "../not-owned",
                }),
            },
            InvalidEnvelopeKind.InvalidNotificationLead => envelope with
            {
                Settings = envelope.Settings with
                {
                    NotificationLeadMinutes = -1,
                },
            },
            InvalidEnvelopeKind.InvalidTheme => envelope with
            {
                Settings = envelope.Settings with { Theme = (AppTheme)999 },
            },
            InvalidEnvelopeKind.InvalidBackdrop => envelope with
            {
                Settings = envelope.Settings with
                {
                    Backdrop = (BackdropKind)999,
                },
            },
            InvalidEnvelopeKind.InvalidCloseBehavior => envelope with
            {
                Settings = envelope.Settings with
                {
                    CloseBehavior = (CloseBehavior)999,
                },
            },
            InvalidEnvelopeKind.BlankName => envelope with
            {
                Games = ImmutableArray.Create(game with { Name = " " }),
            },
            InvalidEnvelopeKind.InvalidBaseStamina => envelope with
            {
                Games = ImmutableArray.Create(game with { BaseStamina = -1 }),
            },
            InvalidEnvelopeKind.InvalidMaxStamina => envelope with
            {
                Games = ImmutableArray.Create(game with { MaxStamina = 0 }),
            },
            InvalidEnvelopeKind.InvalidRecoveryMinutes => envelope with
            {
                Games = ImmutableArray.Create(game with
                {
                    RecoveryMinutes = 0,
                }),
            },
            InvalidEnvelopeKind.InvalidFullTime => envelope with
            {
                Games = ImmutableArray.Create(game with
                {
                    BaseStamina = 0,
                    MaxStamina = 2,
                    RecoveryMinutes = 1,
                    RecordedAtUtc = DateTimeOffset.MaxValue,
                }),
            },
            InvalidEnvelopeKind.EmptyGameId => envelope with
            {
                Games = ImmutableArray.Create(game with { Id = Guid.Empty }),
            },
            InvalidEnvelopeKind.MissingSettings => envelope with
            {
                Settings = null!,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    public enum InvalidEnvelopeKind
    {
        TooManyGames,
        DuplicateGameId,
        InvalidSortOrder,
        InvalidAssetId,
        InvalidNotificationLead,
        InvalidTheme,
        InvalidBackdrop,
        InvalidCloseBehavior,
        BlankName,
        InvalidBaseStamina,
        InvalidMaxStamina,
        InvalidRecoveryMinutes,
        InvalidFullTime,
        EmptyGameId,
        MissingSettings,
    }

    private sealed class TestAppDataPathProvider(string rootPath)
        : IAppDataPathProvider
    {
        public string DataRootPath { get; } = rootPath;
    }
}
