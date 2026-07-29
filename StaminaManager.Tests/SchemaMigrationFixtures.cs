using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Persistence;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StaminaManager.Tests;

internal static class SchemaMigrationFixtures
{
    private static readonly Guid ExistingGameId =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static readonly Guid UnknownSelectedGameId =
        Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static DataEnvelope CreateSchema2WithUnknownSelectedGame() => new(
        DataEnvelope.CurrentSchemaVersion,
        ImmutableArray.Create(new GameEntry(
            ExistingGameId,
            "Current",
            BaseStamina: 10,
            MaxStamina: 100,
            RecoveryMinutes: 8,
            new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero),
            ImageAssetId: null,
            SortOrder: 0,
            RecoverySeconds: 30,
            IsNotificationEnabled: false)),
        AppSettings.CreateDefault(AppTheme.Light) with
        {
            SelectedCompactGameId = UnknownSelectedGameId,
        });

    public static string CreateSchema2JsonWithUnknownSelectedGame() =>
        JsonSerializer.Serialize(
            CreateSchema2WithUnknownSelectedGame(),
            JsonSerializationContext.Configured.DataEnvelope);

    public static string WithUnknownSelectedGame(string json)
    {
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        root["settings"]!["selectedCompactGameId"] = UnknownSelectedGameId;
        return root.ToJsonString();
    }
}
