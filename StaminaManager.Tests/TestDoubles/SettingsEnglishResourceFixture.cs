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
            ["SettingsBackupBusy"] =
                "A backup operation is already in progress. Try again when it is complete.",
            ["SettingsBackupBusyTitle"] =
                "Backup in progress",
            ["SettingsBackupExportBusy"] =
                "Creating a backup...",
            ["SettingsBackupExportSuccessStatus"] =
                "Backup created.",
            ["SettingsBackupExportSuccessMessage"] =
                "The backup was saved to the selected location.",
            ["SettingsBackupExportSuccessTitle"] =
                "Backup completed",
            ["SettingsBackupExportFailureStatus"] =
                "The backup could not be created.",
            ["SettingsBackupExportFailureMessage"] =
                "The backup could not be created. Try again.",
            ["SettingsBackupExportFailureTitle"] =
                "Backup failed",
            ["SettingsBackupPreviewBusy"] =
                "Checking the backup contents...",
            ["SettingsBackupImportFailureStatus"] =
                "The backup could not be read.",
            ["SettingsBackupCancelBusy"] =
                "Canceling backup restore preparation...",
            ["SettingsBackupCancelFailureStatus"] =
                "Backup restore preparation could not be canceled.",
            ["SettingsBackupCancelFailureMessage"] =
                "The backup restore preparation could not be canceled. Try again.",
            ["SettingsBackupCancelFailureTitle"] =
                "Cancel failed",
            ["SettingsBackupRestoreBusy"] =
                "Restoring the backup...",
            ["SettingsBackupRestoreSuccessStatus"] =
                "Backup restored.",
            ["SettingsBackupRestoreSuccessRestartStatus"] =
                "Backup restored. Restart the app to apply the display language.",
            ["SettingsBackupRestorePartialStatus"] =
                "Data restored. Windows settings will be retried next time.",
            ["SettingsBackupRestorePartialRestartStatus"] =
                "Data restored. Windows settings will be retried next time. Restart the app to apply the display language.",
            ["SettingsBackupRestoreSuccessTitle"] =
                "Restore completed",
            ["SettingsBackupRestorePartialTitle"] =
                "Data restored",
            ["SettingsBackupRestoreFailureStatus"] =
                "The backup could not be restored.",
            ["SettingsBackupImportFailureMessage"] =
                "The selected backup could not be read. Choose another backup and try again.",
            ["SettingsBackupImportFailureTitle"] =
                "Backup could not be read",
            ["SettingsBackupRestoreFailureMessage"] =
                "The backup could not be restored. Your current data was not replaced.",
            ["SettingsBackupRestoreFailureTitle"] =
                "Restore failed",
            ["SettingsBackupPrepareMessage"] =
                "This feature is being prepared. No data or Windows settings were changed.",
            ["SettingsBackupPrepareTitle"] =
                "Feature not ready",
            ["SettingsBackupUnavailable"] =
                "Backup is unavailable.",
            ["SettingsBackupAlreadyBusy"] =
                "A backup operation is already running.",
            ["SettingsRestorePreviewDescription"] =
                "The current games, images, and settings will be replaced with the backup. Device-specific notification records will be kept.",
            ["SettingsRestorePreviewGameCountFormat"] = "Games: {0}",
            ["SettingsRestorePreviewImageCountFormat"] = "Images: {0}",
            ["SettingsRestorePreviewThemeFormat"] = "Theme: {0} -> {1}",
            ["SettingsRestorePreviewBackdropFormat"] =
                "Background: {0} -> {1}",
            ["SettingsRestorePreviewNotificationsFormat"] =
                "Notifications: {0} -> {1}",
            ["SettingsRestorePreviewCloseBehaviorFormat"] =
                "Close behavior: {0} -> {1}",
            ["SettingsRestorePreviewAcrylicOpacityFormat"] =
                "Acrylic tint opacity: {0}% -> {1}%",
            ["SettingsRestorePreviewLanguageFormat"] =
                "Language: {0} -> {1}",
            ["SettingsRestorePreviewStartupFormat"] =
                "Startup: {0} -> {1}",
            ["SettingsRestorePreviewOn"] = "Enabled",
            ["SettingsRestorePreviewOff"] = "Disabled",
            ["SettingsRestorePreviewLight"] = "Light",
            ["SettingsRestorePreviewDark"] = "Dark",
            ["SettingsRestorePreviewMica"] = "Mica",
            ["SettingsRestorePreviewAcrylic"] = "Acrylic",
            ["SettingsRestorePreviewTray"] = "Tray",
            ["SettingsRestorePreviewExit"] = "Exit",
            ["SettingsRestorePreviewJapanese"] = "Japanese",
            ["SettingsRestorePreviewEnglish"] = "English",
        });
}
