using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using System.Collections.Immutable;
using System.Text.Json;

namespace StaminaManager.Infrastructure.Persistence;

internal sealed record DecodedDataEnvelope(
    int SourceSchemaVersion,
    DataEnvelope Envelope,
    bool WasBackdropNormalized)
{
    public bool RequiresWriteback => WasBackdropNormalized
        || SourceSchemaVersion < DataEnvelope.CurrentSchemaVersion;
}

internal static class DataEnvelopeCodec
{
    private const int LegacySchema1Version = 1;
    private const int LegacySchema2Version = 2;

    public static DecodedDataEnvelope Deserialize(
        JsonElement root,
        AppLanguage legacyLanguage = AppLanguage.Japanese)
    {
        int sourceSchemaVersion = ReadSchemaVersion(root);
        DataEnvelope envelope = sourceSchemaVersion switch
        {
            LegacySchema1Version => UpgradeLegacySchema1Envelope(
                root.Deserialize(
                    JsonSerializationContext.Configured
                        .LegacySchema1DataEnvelope)
                ?? throw new JsonException("The data JSON is empty."),
                legacyLanguage),
            LegacySchema2Version => UpgradeLegacySchema2Envelope(
                root.Deserialize(
                    JsonSerializationContext.Configured
                        .LegacySchema2DataEnvelope)
                ?? throw new JsonException("The data JSON is empty."),
                legacyLanguage),
            DataEnvelope.CurrentSchemaVersion =>
                DeserializeSchema3(root),
            _ => throw new InvalidDataException(
                "The data schema is not supported."),
        };

        BackdropKind sourceBackdrop = envelope.Settings?.Backdrop
            ?? throw new InvalidDataException("The app settings are invalid.");
        DataEnvelope normalized = NormalizeAndValidate(envelope);
        return new DecodedDataEnvelope(
            sourceSchemaVersion,
            normalized,
            sourceBackdrop != normalized.Settings.Backdrop);
    }

    public static DataEnvelope NormalizeAndValidate(DataEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.SchemaVersion != DataEnvelope.CurrentSchemaVersion
            || envelope.Games.IsDefault
            || envelope.Games.Length > GameEntryValidator.MaxGameCount
            || envelope.Settings is null)
        {
            throw new InvalidDataException("The data envelope is invalid.");
        }

        ValidateSettings(envelope.Settings);
        HashSet<Guid> ids = [];
        HashSet<int> sortOrders = [];
        ImmutableArray<GameEntry>.Builder games =
            ImmutableArray.CreateBuilder<GameEntry>(envelope.Games.Length);
        foreach (GameEntry? game in envelope.Games)
        {
            if (game is null
                || game.Id == Guid.Empty
                || !ids.Add(game.Id)
                || game.SortOrder < 0
                || game.SortOrder >= envelope.Games.Length
                || !sortOrders.Add(game.SortOrder)
                || !IsValidAssetId(game.ImageAssetId))
            {
                throw new InvalidDataException("A game entry is invalid.");
            }

            DateTimeOffset recordedAtUtc =
                game.RecordedAtUtc.ToUniversalTime();
            ValidationResult validation = GameEntryValidator.Validate(
                new GameDraft(
                    game.Name,
                    game.BaseStamina,
                    game.MaxStamina,
                    game.RecoveryMinutes,
                    game.ImageAssetId,
                    game.RecoverySeconds,
                    game.IsNotificationEnabled,
                    game.NotificationLeadMinutesOverride),
                recordedAtUtc);
            if (!validation.IsValid)
            {
                throw new InvalidDataException("A game entry is invalid.");
            }

            games.Add(game with { RecordedAtUtc = recordedAtUtc });
        }

        if (envelope.Settings.SelectedCompactGameId is Guid selected
            && !ids.Contains(selected))
        {
            throw new InvalidDataException(
                "The selected game does not exist.");
        }

        AppSettings normalizedSettings = envelope.Settings with
        {
            Backdrop = BackdropPolicy.NormalizeLegacy(
                envelope.Settings.Backdrop),
        };
        return envelope with
        {
            Games = games.MoveToImmutable(),
            Settings = normalizedSettings,
        };
    }

    private static int ReadSchemaVersion(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The data root must be an object.");
        }

        int schemaVersionCount = 0;
        int schemaVersion = 0;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!property.NameEquals("schemaVersion"))
            {
                continue;
            }

            schemaVersionCount++;
            if (property.Value.ValueKind != JsonValueKind.Number
                || !property.Value.TryGetInt32(out schemaVersion))
            {
                throw new JsonException(
                    "schemaVersion must be a 32-bit integer.");
            }
        }

        if (schemaVersionCount != 1)
        {
            throw new JsonException(
                "schemaVersion must occur exactly once.");
        }

        return schemaVersion;
    }

    private static DataEnvelope DeserializeSchema3(JsonElement root)
    {
        EnsureSchema3SettingsFields(root);
        return root.Deserialize(JsonSerializationContext.Configured.DataEnvelope)
            ?? throw new JsonException("The data JSON is empty.");
    }

    private static DataEnvelope UpgradeLegacySchema1Envelope(
        LegacySchema1DataEnvelope legacy,
        AppLanguage language)
    {
        if (legacy.SchemaVersion != LegacySchema1Version
            || legacy.Games.IsDefault
            || legacy.Settings is null)
        {
            throw new InvalidDataException("The legacy data is invalid.");
        }

        ImmutableArray<GameEntry>.Builder games =
            ImmutableArray.CreateBuilder<GameEntry>(legacy.Games.Length);
        foreach (LegacySchema1GameEntry? game in legacy.Games)
        {
            if (game is null)
            {
                throw new InvalidDataException(
                    "A legacy game entry is invalid.");
            }

            games.Add(new GameEntry(
                game.Id,
                game.Name,
                game.BaseStamina,
                game.MaxStamina,
                game.RecoveryMinutes,
                game.RecordedAtUtc,
                game.ImageAssetId,
                game.SortOrder,
                RecoverySeconds: 0,
                IsNotificationEnabled: true));
        }

        return new DataEnvelope(
            DataEnvelope.CurrentSchemaVersion,
            games.MoveToImmutable(),
            UpgradeSettings(legacy.Settings, language));
    }

    private static DataEnvelope UpgradeLegacySchema2Envelope(
        LegacySchema2DataEnvelope legacy,
        AppLanguage language)
    {
        if (legacy.SchemaVersion != LegacySchema2Version
            || legacy.Games.IsDefault
            || legacy.Settings is null)
        {
            throw new InvalidDataException("The legacy data is invalid.");
        }

        return new DataEnvelope(
            DataEnvelope.CurrentSchemaVersion,
            legacy.Games,
            UpgradeSettings(legacy.Settings, language));
    }

    private static AppSettings UpgradeSettings(
        LegacySchema1And2AppSettings settings,
        AppLanguage language) => new(
            settings.Theme,
            settings.Backdrop,
            settings.NotificationsEnabled,
            settings.NotificationLeadMinutes,
            settings.CloseBehavior,
            settings.StartupEnabled,
            settings.LastDisplayMode,
            settings.SelectedCompactGameId,
            AcrylicOpacityPolicy.DefaultAcrylicTintOpacityPercent,
            language);

    private static void EnsureSchema3SettingsFields(JsonElement root)
    {
        if (!root.TryGetProperty("settings", out JsonElement settings)
            || settings.ValueKind != JsonValueKind.Object
            || !settings.TryGetProperty(
                "acrylicTintOpacityPercent",
                out _)
            || !settings.TryGetProperty("language", out _))
        {
            throw new JsonException("The schema 3 settings are incomplete.");
        }
    }

    private static void ValidateSettings(AppSettings settings)
    {
        if (!Enum.IsDefined(settings.Theme)
            || !Enum.IsDefined(settings.Backdrop)
            || !Enum.IsDefined(settings.CloseBehavior)
            || !Enum.IsDefined(settings.LastDisplayMode)
            || !Enum.IsDefined(settings.Language)
            || settings.SelectedCompactGameId == Guid.Empty
            || !AcrylicOpacityPolicy.IsValid(
                settings.AcrylicTintOpacityPercent)
            || settings.NotificationLeadMinutes is < 0
                or > GameEntryValidator.MaxRecoveryMinutes)
        {
            throw new InvalidDataException("The app settings are invalid.");
        }
    }

    private static bool IsValidAssetId(string? assetId)
    {
        if (assetId is null)
        {
            return true;
        }

        return Guid.TryParseExact(assetId, "N", out Guid parsed)
            && string.Equals(
                assetId,
                parsed.ToString("N"),
                StringComparison.Ordinal);
    }
}
