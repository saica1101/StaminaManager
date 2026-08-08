using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Infrastructure.Backup;
using StaminaManager.Tests.TestDoubles;
using System.Collections.Immutable;

namespace StaminaManager.Tests.Backup;

[TestClass]
public sealed class BackupCoordinatorTests_ResumeWorkflow
{
    [TestMethod]
    public async Task Resume_StartupOn成功をWindowsへ適用してjournalを完了する()
    {
        ResumeStartupService startup = new(StartupState.Disabled)
        {
            NextChange = new StartupChangeResult(
                new StartupStatus(StartupState.Enabled),
                IsApplied: true,
                StartupFailureReason.None),
        };

        await using ResumeWorkflowResult result = await RunResumeAsync(
            desiredStartupEnabled: true,
            startup,
            new ResumeDerivedServices());

        CollectionAssert.AreEqual(new[] { true }, startup.SetRequests);
        Assert.AreEqual(0, startup.GetStatusCount);
        Assert.IsTrue(result.Manager.CurrentData.Settings.StartupEnabled);
        Assert.IsTrue(result.App.IsStartupSynchronized);
        Assert.IsNull(await result.Backup.ResumeAsync(
            CancellationToken.None));
    }

    [TestMethod]
    public async Task Resume_StartupOn拒否時はOff保存してretryを維持する()
    {
        ResumeStartupService startup = new(StartupState.Disabled)
        {
            NextChange = new StartupChangeResult(
                new StartupStatus(StartupState.DisabledByUser),
                IsApplied: false,
                StartupFailureReason.DisabledByUser),
        };

        await using ResumeWorkflowResult result = await RunResumeAsync(
            desiredStartupEnabled: true,
            startup,
            new ResumeDerivedServices());

        CollectionAssert.AreEqual(new[] { true }, startup.SetRequests);
        Assert.IsFalse(result.Manager.CurrentData.Settings.StartupEnabled);
        Assert.IsFalse(result.App.IsStartupSynchronized);
        Assert.AreEqual(
            StartupFailureReason.DisabledByUser,
            result.App.StartupReconcileFailureReason);
        Assert.IsNotNull(await result.Backup.ResumeAsync(
            CancellationToken.None));
    }

    [TestMethod]
    public async Task Resume_StartupOff成功をWindowsへ適用してjournalを完了する()
    {
        ResumeStartupService startup = new(StartupState.Enabled)
        {
            NextChange = new StartupChangeResult(
                new StartupStatus(StartupState.Disabled),
                IsApplied: true,
                StartupFailureReason.None),
        };

        await using ResumeWorkflowResult result = await RunResumeAsync(
            desiredStartupEnabled: false,
            startup,
            new ResumeDerivedServices());

        CollectionAssert.AreEqual(new[] { false }, startup.SetRequests);
        Assert.AreEqual(0, startup.GetStatusCount);
        Assert.IsFalse(result.Manager.CurrentData.Settings.StartupEnabled);
        Assert.IsTrue(result.App.IsStartupSynchronized);
        Assert.IsNull(await result.Backup.ResumeAsync(
            CancellationToken.None));
    }

    [TestMethod]
    public async Task Resume_DerivedStateを固定順で一度だけ調整する()
    {
        ResumeDerivedServices services = new();
        ResumeStartupService startup = new(StartupState.Disabled)
        {
            NextChange = new StartupChangeResult(
                new StartupStatus(StartupState.Enabled),
                IsApplied: true,
                StartupFailureReason.None),
            Calls = services.Calls,
        };

        await using ResumeWorkflowResult result = await RunResumeAsync(
            desiredStartupEnabled: true,
            startup,
            services);

        CollectionAssert.AreEqual(
            new[]
            {
                "Theme",
                "Backdrop",
                "Window",
                "Startup",
                "Notifications",
            },
            services.Calls);
        Assert.IsNull(await result.Backup.ResumeAsync(
            CancellationToken.None));
    }

    [TestMethod]
    public async Task Resume_Notification失敗時はjournalのretryを維持する()
    {
        ResumeDerivedServices services = new()
        {
            ShouldFailNotifications = true,
        };
        ResumeStartupService startup = new(StartupState.Disabled);

        await using ResumeWorkflowResult result = await RunResumeAsync(
            desiredStartupEnabled: false,
            startup,
            services);

        Assert.IsTrue(
            result.App.LastNotificationReconcileResult!.HasFailures);
        Assert.IsNotNull(await result.Backup.ResumeAsync(
            CancellationToken.None));
    }

    private static async Task<ResumeWorkflowResult> RunResumeAsync(
        bool desiredStartupEnabled,
        ResumeStartupService startup,
        ResumeDerivedServices services)
    {
        RestoreWorkflowTestStore source =
            await RestoreWorkflowTestStore.CreateAsync(
                "new",
                desiredStartupEnabled);
        string backupPath = await source.ExportAsync();
        RestoreWorkflowTestStore destination =
            await RestoreWorkflowTestStore.CreateAsync(
                "old",
                startupEnabled: !desiredStartupEnabled);
        BackupCoordinator crashingBackup = destination.CreateBackup(
            stage =>
            {
                if (stage == RestoreJournalStage.LocalCommitted)
                {
                    throw new InvalidOperationException("simulated crash");
                }
            });
        PreparedBackupRestore prepared =
            await crashingBackup.PrepareRestoreAsync(
                backupPath,
                CancellationToken.None);
        GameManager crashingManager = destination.CreateManager();
        await crashingManager.InitializeAsync(
            (await destination.Store.LoadAsync(
                CancellationToken.None)).Envelope!,
            CancellationToken.None);
        RestoreCoordinator crashingRestore = new(
            crashingBackup,
            crashingManager);
        BackupRestoreResult committed =
            await crashingRestore.CommitPreparedAsync(
                prepared.SessionId,
                isReplacementConfirmed: true,
                CancellationToken.None);
        Assert.IsTrue(committed.IsPartial);

        GameManager manager = destination.CreateManager();
        BackupCoordinator backup = destination.CreateBackup();
        AppCoordinator app = new(
            destination.Store,
            manager,
            new RecordingUiDispatcher(),
            services,
            services,
            startup,
            services,
            services,
            new RestoreCoordinator(backup, manager));

        await app.InitializeAsync(CancellationToken.None);
        return new ResumeWorkflowResult(
            source,
            destination,
            manager,
            backup,
            app);
    }

    private sealed record ResumeWorkflowResult(
        RestoreWorkflowTestStore Source,
        RestoreWorkflowTestStore Destination,
        GameManager Manager,
        BackupCoordinator Backup,
        AppCoordinator App) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Source.DisposeAsync();
            await Destination.DisposeAsync();
        }
    }

    private sealed class ResumeDerivedServices
        : IThemeService,
          IBackdropService,
          IWindowStateService,
          INotificationReconciler
    {
        public List<string> Calls { get; } = [];

        public bool ShouldFailNotifications { get; init; }

        public AppDisplayMode CurrentDisplayMode { get; private set; }

        public AppTheme ResolveInitialTheme() => AppTheme.Light;

        public ThemeResult Apply(AppTheme requestedTheme)
        {
            Calls.Add("Theme");
            return new ThemeResult(
                requestedTheme,
                requestedTheme,
                IsApplied: true,
                ErrorMessage: null);
        }

        public BackdropResult Apply(BackdropRequest request)
        {
            Calls.Add("Backdrop");
            return new BackdropResult(
                request.Kind,
                request.Kind,
                BackdropFallbackReason.None,
                ErrorMessage: null,
                request.Kind == BackdropKind.Acrylic
                    ? request.AcrylicTintOpacityPercent
                    : null);
        }

        public void ApplyDisplayMode(AppDisplayMode displayMode)
        {
            Calls.Add("Window");
            CurrentDisplayMode = displayMode;
        }

        public void CaptureCurrent()
        {
        }

        public Task<NotificationReconcileResult> ReconcileAsync(
            IReadOnlyCollection<GameEntry> games,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            Calls.Add("Notifications");
            return Task.FromResult(ShouldFailNotifications
                ? new NotificationReconcileResult(
                    ImmutableArray.Create(new NotificationReconcileIssue(
                        GameId: null,
                        NotificationDecisionError.None,
                        "InjectedFailure")))
                : NotificationReconcileResult.Success);
        }
    }

    private sealed class ResumeStartupService(StartupState initialState)
        : IStartupService
    {
        public List<bool> SetRequests { get; } = [];

        public List<string>? Calls { get; init; }

        public StartupChangeResult? NextChange { get; init; }

        public int GetStatusCount { get; private set; }

        public Task<StartupStatus> GetStatusAsync(
            CancellationToken cancellationToken)
        {
            GetStatusCount++;
            return Task.FromResult(new StartupStatus(initialState));
        }

        public Task<StartupChangeResult> SetEnabledAsync(
            bool isEnabled,
            CancellationToken cancellationToken)
        {
            Calls?.Add("Startup");
            SetRequests.Add(isEnabled);
            return Task.FromResult(NextChange ?? new StartupChangeResult(
                new StartupStatus(isEnabled
                    ? StartupState.Enabled
                    : StartupState.Disabled),
                IsApplied: true,
                StartupFailureReason.None));
        }
    }
}
