using StaminaManager.Core.Models;
using System.Collections.Immutable;

namespace StaminaManager.Infrastructure.Persistence;

internal sealed record LegacySchema1DataEnvelope(
    int SchemaVersion,
    ImmutableArray<LegacySchema1GameEntry> Games,
    LegacySchema1And2AppSettings Settings);

internal sealed record LegacySchema1GameEntry(
    Guid Id,
    string Name,
    int BaseStamina,
    int MaxStamina,
    int RecoveryMinutes,
    DateTimeOffset RecordedAtUtc,
    string? ImageAssetId,
    int SortOrder);

internal sealed record LegacySchema1And2AppSettings(
    AppTheme Theme,
    BackdropKind Backdrop,
    bool NotificationsEnabled,
    int NotificationLeadMinutes,
    CloseBehavior CloseBehavior,
    bool StartupEnabled,
    AppDisplayMode LastDisplayMode = AppDisplayMode.Standard,
    Guid? SelectedCompactGameId = null);

internal sealed record LegacySchema2DataEnvelope(
    int SchemaVersion,
    ImmutableArray<GameEntry> Games,
    LegacySchema1And2AppSettings Settings);
