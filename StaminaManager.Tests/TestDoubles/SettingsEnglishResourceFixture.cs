namespace StaminaManager.Tests.TestDoubles;

internal static class SettingsEnglishResourceFixture
{
    public static RecordingResourceService Create() =>
        new(new Dictionary<string, string>
        {
            ["SettingsErrorTitle"] = "Could not complete the setting",
            ["SettingsUnexpectedFailure"] =
                "The setting could not be changed. Try again.",
            ["SettingsNotReady"] =
                "Settings are still loading. Try again when loading is complete.",
            ["SettingsBackdropFallbackHighContrast"] =
                "The background cannot be used in high contrast. "
                + "Review contrast settings or keep a solid background.",
            ["SettingsBackdropFallbackSolidFailed"] =
                "The solid background could not be applied. Restart the app.",
            ["SettingsBackdropFallbackTitle"] =
                "Switched to a solid background",
            ["SettingsAcrylicOpacityFailureRollbackFailed"] =
                "Acrylic tint opacity could not be changed. "
                + "The background could not be restored, so restart the app.",
            ["SettingsAcrylicOpacityRollbackSafeFallback"] =
                "Acrylic tint opacity could not be saved. "
                + "The background was switched to a safe solid background. "
                + "Restart the app.",
            ["SettingsLanguageRestartRequired"] =
                "The language changed. Restart the app to apply it.",
            ["SettingsLanguageInconsistent"] =
                "The language setting was saved, but Windows could not apply it. "
                + "It will be retried at the next startup.",
            ["SettingsStartupDisabledByPolicy"] =
                "Windows startup cannot be enabled because of an organization "
                + "policy. Contact your administrator if needed.",
            ["SettingsStartupChangeFailure"] =
                "Windows startup could not be changed. Try again later.",
            ["SettingsStartupSaveFailureActualState"] =
                "Could not save the setting. The previous setting was restored. "
                + "The actual Windows state is shown. Try again.",
            ["NotificationAvailabilityDisabledForApplication"] =
                "Notifications are disabled for this app in Windows settings.",
            ["SettingsNotificationPermissionDisabled"] =
                "Windows notifications are disabled. Open Windows notification "
                + "settings and enable them.",
            ["SettingsNotificationSettingsLaunchFailure"] =
                "Windows notification settings could not be opened. "
                + "Check notifications manually in Windows Settings.",
            ["SettingsNotificationReconcileFailure"] =
                "Settings were saved, but some Windows notifications could not "
                + "be synchronized. Change the setting and try again.",
            ["SettingsNotificationReconcileTitle"] =
                "Notification synchronization is incomplete",
        });
}
