using StaminaManager.Core.Persistence;
using System.Text.Json.Serialization;

namespace StaminaManager.Infrastructure.Persistence;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    UseStringEnumConverter = true,
    WriteIndented = false)]
[JsonSerializable(typeof(DataEnvelope))]
internal sealed partial class JsonSerializationContext
    : JsonSerializerContext;
