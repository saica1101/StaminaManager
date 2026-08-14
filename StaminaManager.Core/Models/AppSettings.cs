namespace StaminaManager.Core.Models;

using StaminaManager.Core.Validation;

public sealed record AppSettings(
    AppTheme Theme,
    BackdropKind Backdrop,
    bool NotificationsEnabled,
    int NotificationLeadMinutes,
    CloseBehavior CloseBehavior,
    bool StartupEnabled,
    AppDisplayMode LastDisplayMode = AppDisplayMode.Standard,
    Guid? SelectedCompactGameId = null,
    int AcrylicTintOpacityPercent =
        AcrylicOpacityPolicy.DefaultAcrylicTintOpacityPercent,
    AppLanguage Language = AppLanguage.Japanese)
{
    public const int MinAcrylicTintOpacityPercent =
        AcrylicOpacityPolicy.MinAcrylicTintOpacityPercent;

    public const int MaxAcrylicTintOpacityPercent =
        AcrylicOpacityPolicy.MaxAcrylicTintOpacityPercent;

    public const int DefaultAcrylicTintOpacityPercent =
        AcrylicOpacityPolicy.DefaultAcrylicTintOpacityPercent;

    public const int MinNotificationLeadMinutes = 0;

    public const int MaxNotificationLeadMinutes = 525_600;

    public static AppSettings CreateDefault(
        AppTheme initialTheme,
        AppLanguage language = AppLanguage.Japanese) => new(
        initialTheme,
        BackdropKind.Mica,
        NotificationsEnabled: true,
        NotificationLeadMinutes: 15,
        CloseBehavior.MinimizeToTray,
        StartupEnabled: false,
        AppDisplayMode.Standard,
        SelectedCompactGameId: null,
        AcrylicTintOpacityPercent: DefaultAcrylicTintOpacityPercent,
        Language: language);
}

public enum AppTheme
{
    System,
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
