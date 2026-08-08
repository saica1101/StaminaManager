using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Persistence;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StaminaManager.Tests.Persistence;

[TestClass]
public sealed class Schema3MigrationTests
{
    [TestMethod]
    [DataRow(0)]
    [DataRow(50)]
    [DataRow(100)]
    public async Task SaveAndLoadAsync_有効なAcrylicOpacityをRoundTripする(
        int opacity)
    {
        using TestDataRoot root = new();
        LocalDataStore store = new(root, new FixedLanguageService(
            AppLanguage.English));
        DataEnvelope data = TestData.CreateEnvelope() with
        {
            Settings = TestData.CreateEnvelope().Settings with
            {
                AcrylicTintOpacityPercent = opacity,
                Language = AppLanguage.English,
            },
        };

        await store.SaveAsync(data, CancellationToken.None);
        DataLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(opacity, loaded.Envelope!.Settings.AcrylicTintOpacityPercent);
        Assert.AreEqual(AppLanguage.English, loaded.Envelope.Settings.Language);
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(101)]
    public async Task LoadAsync_範囲外Opacityを拒否する(int opacity)
    {
        using TestDataRoot root = new();
        LocalDataStore store = new(root);
        JsonObject json = TestData.CreateSchema3Json();
        json["settings"]!["acrylicTintOpacityPercent"] = opacity;
        Directory.CreateDirectory(root.DataRootPath);
        await File.WriteAllTextAsync(root.PrimaryPath, json.ToJsonString());

        DataLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Corrupt, loaded.Status);
    }

    [TestMethod]
    public async Task LoadAsync_未知Languageを拒否する()
    {
        using TestDataRoot root = new();
        LocalDataStore store = new(root);
        JsonObject json = TestData.CreateSchema3Json();
        json["settings"]!["language"] = "Klingon";
        Directory.CreateDirectory(root.DataRootPath);
        await File.WriteAllTextAsync(root.PrimaryPath, json.ToJsonString());

        DataLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Corrupt, loaded.Status);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public async Task LoadAsync_旧SchemaをSchema3へ移行してAtomicWritebackする(
        int sourceSchema)
    {
        using TestDataRoot root = new();
        LocalDataStore store = new(root, new FixedLanguageService(
            AppLanguage.English));
        Directory.CreateDirectory(root.DataRootPath);
        string legacy = TestData.CreateLegacyJson(sourceSchema);
        await File.WriteAllTextAsync(root.PrimaryPath, legacy);

        DataLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Primary, loaded.Status);
        Assert.AreEqual(3, loaded.Envelope!.SchemaVersion);
        Assert.AreEqual(80, loaded.Envelope.Settings.AcrylicTintOpacityPercent);
        Assert.AreEqual(AppLanguage.English, loaded.Envelope.Settings.Language);
        using JsonDocument primary = JsonDocument.Parse(
            await File.ReadAllTextAsync(root.PrimaryPath));
        Assert.AreEqual(3, primary.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(legacy, await File.ReadAllTextAsync(root.RecoveryPath));
    }

    [TestMethod]
    public async Task LoadAsync_旧SchemaRecoveryは読込時に書換えない()
    {
        using TestDataRoot root = new();
        LocalDataStore store = new(root);
        Directory.CreateDirectory(root.DataRootPath);
        const string corruptPrimary = "{broken";
        string legacyRecovery = TestData.CreateLegacyJson(2);
        await File.WriteAllTextAsync(root.PrimaryPath, corruptPrimary);
        await File.WriteAllTextAsync(root.RecoveryPath, legacyRecovery);

        DataLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Recovery, loaded.Status);
        Assert.AreEqual(3, loaded.Envelope!.SchemaVersion);
        Assert.AreEqual(legacyRecovery, await File.ReadAllTextAsync(root.RecoveryPath));
    }

    [TestMethod]
    public async Task PromoteRecoveryAsync_旧SchemaRecoveryをSchema3として昇格する()
    {
        using TestDataRoot root = new();
        LocalDataStore store = new(root, new FixedLanguageService(
            AppLanguage.English));
        Directory.CreateDirectory(root.DataRootPath);
        await File.WriteAllTextAsync(root.PrimaryPath, "{broken");
        await File.WriteAllTextAsync(root.RecoveryPath, TestData.CreateLegacyJson(2));

        RecoveryPromotionResult promoted = await store.PromoteRecoveryAsync(
            CancellationToken.None);

        Assert.AreEqual(3, promoted.Envelope.SchemaVersion);
        Assert.AreEqual(AppLanguage.English, promoted.Envelope.Settings.Language);
        using JsonDocument primary = JsonDocument.Parse(
            await File.ReadAllTextAsync(root.PrimaryPath));
        Assert.AreEqual(3, primary.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [TestMethod]
    public async Task LoadAsync_旧Schemaの書戻し失敗は汎用Warningで継続する()
    {
        using TestDataRoot root = new();
        LocalDataStore store = new(root);
        Directory.CreateDirectory(root.DataRootPath);
        string legacy = TestData.CreateLegacyJson(2);
        await File.WriteAllTextAsync(root.PrimaryPath, legacy);
        await using FileStream blocker = new(
            root.PrimaryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

        DataLoadResult loaded = await store.LoadAsync(CancellationToken.None);

        Assert.AreEqual(DataLoadStatus.Primary, loaded.Status);
        Assert.AreEqual(3, loaded.Envelope!.SchemaVersion);
        Assert.AreEqual(
            DataLoadWarning.SchemaMigrationWritebackFailed,
            loaded.Warning);
        Assert.AreEqual(legacy, await File.ReadAllTextAsync(root.PrimaryPath));
    }

    [TestMethod]
    public void LegacyDtos_現行AppSettingsを参照しない()
    {
        Type settings = typeof(LegacySchema1And2AppSettings);
        Assert.AreEqual(settings, typeof(LegacySchema1DataEnvelope)
            .GetProperty("Settings")!.PropertyType);
        Assert.AreEqual(settings, typeof(LegacySchema2DataEnvelope)
            .GetProperty("Settings")!.PropertyType);
    }

    private sealed class FixedLanguageService(AppLanguage language)
        : IAppLanguageService
    {
        public AppLanguage GetEffectiveLanguage() => language;

        public LanguageChangeResult SetLanguage(AppLanguage language) =>
            new(language, IsApplied: true, LanguageFailureReason.None);
    }
}

internal sealed class TestDataRoot : IAppDataPathProvider, IDisposable
{
    public TestDataRoot()
    {
        DataRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    }

    public string DataRootPath { get; }

    public string PrimaryPath => Path.Combine(DataRootPath, "data.json");

    public string RecoveryPath => Path.Combine(DataRootPath, "data.recovery.json");

    public void Dispose()
    {
        if (Directory.Exists(DataRootPath))
        {
            Directory.Delete(DataRootPath, recursive: true);
        }
    }
}

internal static class TestData
{
    public static DataEnvelope CreateEnvelope() => new(
        DataEnvelope.CurrentSchemaVersion,
        [],
        AppSettings.CreateDefault(AppTheme.Light));

    public static JsonObject CreateSchema3Json() => JsonNode.Parse(
        JsonSerializer.Serialize(
            CreateEnvelope(),
            JsonSerializationContext.Configured.DataEnvelope))!.AsObject();

    public static string CreateLegacyJson(int schemaVersion) => schemaVersion == 1
        ? """
          {"schemaVersion":1,"games":[],"settings":{"theme":"Light","backdrop":"Mica","notificationsEnabled":true,"notificationLeadMinutes":15,"closeBehavior":"MinimizeToTray","startupEnabled":false,"lastDisplayMode":"Standard","selectedCompactGameId":null}}
          """
        : """
          {"schemaVersion":2,"games":[],"settings":{"theme":"Light","backdrop":"Mica","notificationsEnabled":true,"notificationLeadMinutes":15,"closeBehavior":"MinimizeToTray","startupEnabled":false,"lastDisplayMode":"Standard","selectedCompactGameId":null}}
          """;
}
