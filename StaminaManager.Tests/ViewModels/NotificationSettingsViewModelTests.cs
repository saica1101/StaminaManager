using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.ViewModels;
using System.Collections.Immutable;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class NotificationSettingsViewModelTests
{
    [TestMethod]
    public async Task SetNotificationsEnabledAsync_PersistsBeforeReconcile()
    {
        RecordingNotificationReconciler reconciler = new();
        SettingsViewModel viewModel = await CreateViewModelAsync(
            reconciler: reconciler,
            resources: SettingsEnglishResourceFixture.Create());

        bool changed = await viewModel.SetNotificationsEnabledAsync(false);

        Assert.IsTrue(changed);
        Assert.IsFalse(viewModel.AreNotificationsEnabled);
        Assert.IsNotNull(reconciler.SettingsAtReconcile);
        Assert.IsFalse(reconciler.SettingsAtReconcile.NotificationsEnabled);
    }

    [TestMethod]
    public async Task SetNotificationLeadMinutesAsync_PartialFailureKeepsData()
    {
        RecordingNotificationReconciler reconciler = new()
        {
            Result = new NotificationReconcileResult(
                ImmutableArray.Create(new NotificationReconcileIssue(
                    Guid.NewGuid(),
                    NotificationDecisionError.None,
                    "InvalidOperationException"))),
        };
        SettingsViewModel viewModel = await CreateViewModelAsync(
            reconciler: reconciler,
            resources: SettingsEnglishResourceFixture.Create());

        bool changed = await viewModel.SetNotificationLeadMinutesAsync(30);

        Assert.IsFalse(changed);
        Assert.AreEqual(30, viewModel.NotificationLeadMinutes);
        Assert.AreEqual(
            30,
            reconciler.SettingsAtReconcile?.NotificationLeadMinutes);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        Assert.AreEqual(InfoBarSeverity.Warning, viewModel.InfoBarSeverity);
        Assert.AreEqual(
            "Settings were saved, but some Windows notifications could not "
            + "be synchronized. Change the setting and try again.",
            viewModel.InfoBarMessage);
    }

    [TestMethod]
    public async Task RefreshNotificationAvailabilityAsync_ShowsRecoveryRoute()
    {
        SettingsViewModel viewModel = await CreateViewModelAsync(
            permissionState:
                NotificationPermissionState.DisabledForApplication,
            resources: SettingsEnglishResourceFixture.Create());

        await viewModel.RefreshNotificationAvailabilityAsync();

        Assert.IsFalse(viewModel.AreWindowsNotificationsAvailable);
        Assert.AreEqual(
            Visibility.Visible,
            viewModel.OpenWindowsNotificationSettingsVisibility);
        Assert.AreEqual(
            "Notifications are disabled for this app in Windows settings.",
            viewModel.NotificationAvailabilityText);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        Assert.AreEqual(
            InfoBarSeverity.Warning,
            viewModel.InfoBarSeverity);
        Assert.AreEqual(
            "Windows notifications are disabled. Open Windows notification "
            + "settings and enable them.",
            viewModel.InfoBarMessage);
    }

    [TestMethod]
    public async Task OpenWindowsNotificationSettingsAsync_FailureUsesInfoBar()
    {
        RecordingSettingsLauncher launcher = new() { Result = false };
        SettingsViewModel viewModel = await CreateViewModelAsync(
            settingsLauncher: launcher,
            resources: SettingsEnglishResourceFixture.Create());

        bool opened =
            await viewModel.OpenWindowsNotificationSettingsAsync();

        Assert.IsFalse(opened);
        Assert.AreEqual(1, launcher.CallCount);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        Assert.AreEqual(InfoBarSeverity.Error, viewModel.InfoBarSeverity);
        Assert.AreEqual(
            "Windows notification settings could not be opened. "
            + "Check notifications manually in Windows Settings.",
            viewModel.InfoBarMessage);
    }

    private static async Task<SettingsViewModel> CreateViewModelAsync(
        RecordingNotificationReconciler? reconciler = null,
        NotificationPermissionState permissionState =
            NotificationPermissionState.Enabled,
        RecordingSettingsLauncher? settingsLauncher = null,
        IAppResourceService? resources = null)
    {
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light);
        InMemoryDataStore store = new();
        GameManager gameManager = new(
            store,
            new FakeClock(DateTimeOffset.UtcNow),
            settings);
        await gameManager.InitializeAsync(
            new DataEnvelope(
                DataEnvelope.CurrentSchemaVersion,
                ImmutableArray<GameEntry>.Empty,
                settings),
            CancellationToken.None);
        SettingsViewModel viewModel = new(
            gameManager,
            new PassThroughThemeService(),
            new PassThroughBackdropService(),
            new PassThroughStartupService(),
            reconciler ?? new RecordingNotificationReconciler(),
            new FixedPermissionService(permissionState),
            settingsLauncher ?? new RecordingSettingsLauncher(),
            resources ?? new RecordingResourceService());
        viewModel.MarkReady();
        return viewModel;
    }

    private sealed class RecordingNotificationReconciler
        : INotificationReconciler
    {
        public NotificationReconcileResult Result { get; init; } =
            NotificationReconcileResult.Success;

        public AppSettings? SettingsAtReconcile { get; private set; }

        public Task<NotificationReconcileResult> ReconcileAsync(
            IReadOnlyCollection<GameEntry> games,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            SettingsAtReconcile = settings;
            return Task.FromResult(Result);
        }
    }

    private sealed class FixedPermissionService(
        NotificationPermissionState state)
        : INotificationPermissionService
    {
        public Task<NotificationPermissionStatus> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new NotificationPermissionStatus(state));
    }

    private sealed class RecordingSettingsLauncher : ISettingsLauncher
    {
        public bool Result { get; init; } = true;

        public int CallCount { get; private set; }

        public Task<bool> OpenNotificationSettingsAsync(
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(Result);
        }
    }

    private sealed class InMemoryDataStore : ILocalDataStore
    {
        public DataEnvelope? Saved { get; private set; }

        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) => throw new
                NotSupportedException();

        public Task SaveAsync(
            DataEnvelope envelope,
            CancellationToken cancellationToken)
        {
            Saved = envelope;
            return Task.CompletedTask;
        }

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken) => throw new
                NotSupportedException();
    }

    private sealed class PassThroughThemeService : IThemeService
    {
        public AppTheme ResolveInitialTheme() => AppTheme.Light;

        public ThemeResult Apply(AppTheme requestedTheme) => new(
            requestedTheme,
            requestedTheme,
            IsApplied: true,
            ErrorMessage: null);
    }

    private sealed class PassThroughBackdropService : IBackdropService
    {
        public BackdropResult Apply(BackdropRequest request) => new(
            request.Kind,
            request.Kind,
            BackdropFallbackReason.None,
            ErrorMessage: null,
            request.Kind == BackdropKind.Acrylic
                ? request.AcrylicTintOpacityPercent
                : null);
    }

    private sealed class PassThroughStartupService : IStartupService
    {
        public Task<StartupStatus> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new StartupStatus(StartupState.Disabled));

        public Task<StartupChangeResult> SetEnabledAsync(
            bool isEnabled,
            CancellationToken cancellationToken) => Task.FromResult(
                new StartupChangeResult(
                    new StartupStatus(
                        isEnabled
                            ? StartupState.Enabled
                            : StartupState.Disabled),
                    IsApplied: true,
                    StartupFailureReason.None));
    }
}
