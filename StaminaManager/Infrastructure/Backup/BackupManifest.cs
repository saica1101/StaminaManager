namespace StaminaManager.Infrastructure.Backup;

public sealed record BackupManifest(
    int SchemaVersion,
    int DataSchemaVersion,
    string DataFile,
    IReadOnlyList<BackupAssetManifest> Assets)
{
    public const int CurrentSchemaVersion = 1;
}

public sealed record BackupAssetManifest(
    string AssetId,
    string EntryPath,
    string MediaType);
