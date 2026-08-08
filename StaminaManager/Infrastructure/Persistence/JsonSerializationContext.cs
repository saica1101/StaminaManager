using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace StaminaManager.Infrastructure.Persistence;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    RespectRequiredConstructorParameters = true,
    WriteIndented = false)]
[JsonSerializable(typeof(DataEnvelope))]
[JsonSerializable(typeof(LegacySchema1DataEnvelope))]
[JsonSerializable(typeof(LegacySchema2DataEnvelope))]
internal sealed partial class JsonSerializationContext
    : JsonSerializerContext
{
    public static JsonSerializationContext Configured { get; } =
        new(CreateOptions());

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            RespectRequiredConstructorParameters = true,
            WriteIndented = false,
        };
        options.Converters.Add(new StrictStringEnumConverter<AppTheme>());
        options.Converters.Add(new StrictStringEnumConverter<AppLanguage>());
        options.Converters.Add(new StrictStringEnumConverter<BackdropKind>());
        options.Converters.Add(new StrictStringEnumConverter<CloseBehavior>());
        options.Converters.Add(
            new StrictStringEnumConverter<AppDisplayMode>());
        return options;
    }
}

internal sealed class StrictStringEnumConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    public override TEnum Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException(
                $"{typeof(TEnum).Name} must be a string.");
        }

        string? value = reader.GetString();
        if (value is null
            || !Enum.TryParse(value, ignoreCase: false, out TEnum parsed)
            || !Enum.IsDefined(parsed)
            || !string.Equals(
                value,
                parsed.ToString(),
                StringComparison.Ordinal))
        {
            throw new JsonException(
                $"{value} is not a valid {typeof(TEnum).Name} value.");
        }

        return parsed;
    }

    public override void Write(
        Utf8JsonWriter writer,
        TEnum value,
        JsonSerializerOptions options)
    {
        if (!Enum.IsDefined(value))
        {
            throw new JsonException(
                $"{value} is not a defined {typeof(TEnum).Name} value.");
        }

        writer.WriteStringValue(value.ToString());
    }
}
