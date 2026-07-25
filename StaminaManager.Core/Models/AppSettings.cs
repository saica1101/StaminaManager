namespace StaminaManager.Core.Models;

public sealed record AppSettings(
    AppTheme Theme,
    BackdropKind Backdrop,
    bool NotificationsEnabled,
    int NotificationLeadMinutes,
    CloseBehavior CloseBehavior,
    bool StartupEnabled,
    AppDisplayMode LastDisplayMode = AppDisplayMode.Standard,
    Guid? SelectedCompactGameId = null)
{
    public const int MinNotificationLeadMinutes = 0;

    public const int MaxNotificationLeadMinutes = 525_600;

    public static AppSettings CreateDefault(AppTheme initialTheme) => new(
        initialTheme,
        BackdropKind.Mica,
        NotificationsEnabled: true,
        NotificationLeadMinutes: 15,
        CloseBehavior.MinimizeToTray,
        StartupEnabled: false,
        AppDisplayMode.Standard,
        SelectedCompactGameId: null);
}

public enum AppTheme
{
    Light,
    Dark,
}

public enum BackdropKind
{
    Mica,
    Acrylic,
    Blur,
    Transparent,
    Solid,
}

public enum CloseBehavior
{
    MinimizeToTray,
    Exit,
}
