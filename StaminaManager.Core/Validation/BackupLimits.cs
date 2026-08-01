namespace StaminaManager.Core.Validation;

public static class BackupLimits
{
    public const long MaxBackupBytes = 512L * 1024 * 1024;
    public const int MaxEntryCount = 202;
    public const int MaxEntryPathLength = 200;
    public const int MaxManifestBytes = 256 * 1024;
    public const int MaxDataJsonBytes = 4 * 1024 * 1024;
    public const int MaxImageCount = 100;
    public const long MaxExpandedBytes = 512L * 1024 * 1024;
    public const double MaxCompressionRatio = 100d;
    public const uint MaxImageDimension = 4096;
}
