using StaminaManager.Core.Models;
using System.Collections.Immutable;

namespace StaminaManager.Infrastructure.Persistence;

internal sealed record LegacyDataEnvelope(
    int SchemaVersion,
    ImmutableArray<LegacyGameEntry> Games,
    AppSettings Settings);

internal sealed record LegacyGameEntry(
    Guid Id,
    string Name,
    int BaseStamina,
    int MaxStamina,
    int RecoveryMinutes,
    DateTimeOffset RecordedAtUtc,
    string? ImageAssetId,
    int SortOrder);
