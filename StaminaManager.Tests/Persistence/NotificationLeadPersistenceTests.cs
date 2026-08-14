using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Infrastructure.Persistence;
using System.Collections.Immutable;
using System.Text.Json;

namespace StaminaManager.Tests.Persistence;

[TestClass]
public sealed class NotificationLeadPersistenceTests
{
    [TestMethod]
    public void Schema3_RoundTripPreservesPerGameNotificationLeadOverride()
    {
        GameEntry game = new(
            Guid.NewGuid(),
            "Test game",
            BaseStamina: 40,
            MaxStamina: 100,
            RecoveryMinutes: 5,
            RecordedAtUtc: new DateTimeOffset(
                2026, 8, 14, 0, 0, 0, TimeSpan.Zero),
            ImageAssetId: null,
            SortOrder: 0,
            RecoverySeconds: 0,
            IsNotificationEnabled: true,
            NotificationLeadMinutesOverride: 30);
        DataEnvelope envelope = new(
            DataEnvelope.CurrentSchemaVersion,
            ImmutableArray.Create(game),
            AppSettings.CreateDefault(AppTheme.Light));

        string json = JsonSerializer.Serialize(
            envelope,
            JsonSerializationContext.Configured.DataEnvelope);
        using JsonDocument document = JsonDocument.Parse(json);

        DecodedDataEnvelope decoded =
            DataEnvelopeCodec.Deserialize(document.RootElement);

        Assert.AreEqual(
            30,
            decoded.Envelope.Games.Single()
                .NotificationLeadMinutesOverride);
        Assert.AreEqual(
            30,
            document.RootElement
                .GetProperty("games")[0]
                .GetProperty("notificationLeadMinutesOverride")
                .GetInt32());
    }

    [TestMethod]
    public void Schema3_RoundTripPreservesNullNotificationLeadOverride()
    {
        GameEntry game = new(
            Guid.NewGuid(),
            "Test game",
            BaseStamina: 40,
            MaxStamina: 100,
            RecoveryMinutes: 5,
            RecordedAtUtc: new DateTimeOffset(
                2026, 8, 14, 0, 0, 0, TimeSpan.Zero),
            ImageAssetId: null,
            SortOrder: 0,
            RecoverySeconds: 0,
            IsNotificationEnabled: true,
            NotificationLeadMinutesOverride: null);
        DataEnvelope envelope = new(
            DataEnvelope.CurrentSchemaVersion,
            ImmutableArray.Create(game),
            AppSettings.CreateDefault(AppTheme.Light));

        string json = JsonSerializer.Serialize(
            envelope,
            JsonSerializationContext.Configured.DataEnvelope);
        using JsonDocument document = JsonDocument.Parse(json);

        DecodedDataEnvelope decoded =
            DataEnvelopeCodec.Deserialize(document.RootElement);

        Assert.IsNull(
            decoded.Envelope.Games.Single()
                .NotificationLeadMinutesOverride);
        Assert.AreEqual(
            JsonValueKind.Null,
            document.RootElement
                .GetProperty("games")[0]
                .GetProperty("notificationLeadMinutesOverride")
                .ValueKind);
    }
}
