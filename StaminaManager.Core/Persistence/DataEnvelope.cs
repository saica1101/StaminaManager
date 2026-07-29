using StaminaManager.Core.Models;
using System.Collections.Immutable;

namespace StaminaManager.Core.Persistence;

public sealed record DataEnvelope(
    int SchemaVersion,
    ImmutableArray<GameEntry> Games,
    AppSettings Settings)
{
    public const int CurrentSchemaVersion = 2;
}

public enum DataLoadStatus
{
    Empty,
    Primary,
    Recovery,
    Corrupt,
}

public sealed record DataLoadResult(
    DataLoadStatus Status,
    DataEnvelope? Envelope,
    string PrimaryPath,
    string RecoveryPath);

public sealed record RecoveryPromotionResult(
    DataEnvelope Envelope,
    string PrimaryPath,
    string RecoveryPath,
    string? DiagnosticBackupPath);
