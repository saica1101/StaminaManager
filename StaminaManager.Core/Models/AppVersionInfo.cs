namespace StaminaManager.Core.Models;

public sealed record AppVersionInfo(
    int Major,
    int Minor,
    int Build,
    int Revision)
{
    public static AppVersionInfo Fallback { get; } = new(0, 0, 0, 0);

    public string DisplayVersion => Revision == 0
        ? $"{Major}.{Minor}.{Build}"
        : $"{Major}.{Minor}.{Build}.{Revision}";
}
