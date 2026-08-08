using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.ViewModels;
using System.Collections.Immutable;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class StartupSettingsTests
{
    [TestMethod]
    public async Task SetStartupEnabledAsync_ユーザー拒否時は実状態へ戻す()
    {
        Context context = await Context.CreateAsync();
        context.StartupService.Results.Enqueue(new StartupChangeResult(
            new StartupStatus(StartupState.DisabledByUser),
            IsApplied: false,
            StartupFailureReason.DisabledByUser));
        SettingsViewModel viewModel = context.CreateViewModel();

        bool changed = await viewModel.SetStartupEnabledAsync(
            isEnabled: true,
            CancellationToken.None);

        Assert.IsFalse(changed);
        Assert.IsFalse(viewModel.IsStartupEnabled);
        Assert.IsFalse(context.Manager.CurrentData.Settings.StartupEnabled);
        Assert.AreEqual(0, context.Store.SaveCount);
        CollectionAssert.AreEqual(
            new[] { true },
            context.StartupService.Requests);
        StringAssert.Contains(viewModel.InfoBarMessage, "ユーザー");
    }

    [TestMethod]
    public async Task SetStartupEnabledAsync_ポリシー拒否時は実状態へ戻す()
    {
        Context context = await Context.CreateAsync();
        context.StartupService.Results.Enqueue(new StartupChangeResult(
            new StartupStatus(StartupState.DisabledByPolicy),
            IsApplied: false,
            StartupFailureReason.DisabledByPolicy));
        SettingsViewModel viewModel = context.CreateViewModel();

        bool changed = await viewModel.SetStartupEnabledAsync(
            isEnabled: true,
            CancellationToken.None);

        Assert.IsFalse(changed);
        Assert.IsFalse(viewModel.IsStartupEnabled);
        Assert.AreEqual(0, context.Store.SaveCount);
        StringAssert.Contains(viewModel.InfoBarMessage, "ポリシー");
        StringAssert.Contains(viewModel.InfoBarMessage, "管理者");
    }

    [TestMethod]
    public async Task SetStartupEnabledAsync_強制有効ポリシー拒否は管理者確認を案内する()
    {
        Context context = await Context.CreateAsync();
        context.StartupService.Results.Enqueue(new StartupChangeResult(
            new StartupStatus(StartupState.EnabledByPolicy),
            IsApplied: false,
            StartupFailureReason.EnabledByPolicy));
        SettingsViewModel viewModel = context.CreateViewModel();

        bool changed = await viewModel.SetStartupEnabledAsync(
            isEnabled: false,
            CancellationToken.None);

        Assert.IsFalse(changed);
        Assert.IsTrue(viewModel.IsStartupEnabled);
        Assert.AreEqual(0, context.Store.SaveCount);
        StringAssert.Contains(viewModel.InfoBarMessage, "管理者");
    }

    [TestMethod]
    public async Task SetStartupEnabledAsync_Os成功後だけ保存する()
    {
        Context context = await Context.CreateAsync();
        context.StartupService.Results.Enqueue(new StartupChangeResult(
            new StartupStatus(StartupState.Enabled),
            IsApplied: true,
            StartupFailureReason.None));
        SettingsViewModel viewModel = context.CreateViewModel();

        bool changed = await viewModel.SetStartupEnabledAsync(
            isEnabled: true,
            CancellationToken.None);

        Assert.IsTrue(changed);
        Assert.IsTrue(viewModel.IsStartupEnabled);
        Assert.IsTrue(context.Manager.CurrentData.Settings.StartupEnabled);
        Assert.AreEqual(1, context.Store.SaveCount);
    }

    [TestMethod]
    public async Task SetStartupEnabledAsync_保存失敗時はOs状態も戻す()
    {
        Context context = await Context.CreateAsync();
        context.StartupService.Results.Enqueue(new StartupChangeResult(
            new StartupStatus(StartupState.Enabled),
            IsApplied: true,
            StartupFailureReason.None));
        context.StartupService.Results.Enqueue(new StartupChangeResult(
            new StartupStatus(StartupState.Disabled),
            IsApplied: true,
            StartupFailureReason.None));
        context.Store.SaveException = new IOException("save detail");
        SettingsViewModel viewModel = context.CreateViewModel();

        bool changed = await viewModel.SetStartupEnabledAsync(
            isEnabled: true,
            CancellationToken.None);

        Assert.IsFalse(changed);
        CollectionAssert.AreEqual(
            new[] { true, false },
            context.StartupService.Requests);
        Assert.IsFalse(viewModel.IsStartupEnabled);
        Assert.IsFalse(context.Manager.CurrentData.Settings.StartupEnabled);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
    }

    [TestMethod]
    public async Task SetStartupEnabledAsync_Rollback拒否時は実状態を表示する()
    {
        Context context = await Context.CreateAsync();
        context.StartupService.Results.Enqueue(new StartupChangeResult(
            new StartupStatus(StartupState.Enabled),
            IsApplied: true,
            StartupFailureReason.None));
        context.StartupService.Results.Enqueue(new StartupChangeResult(
            new StartupStatus(StartupState.EnabledByPolicy),
            IsApplied: false,
            StartupFailureReason.EnabledByPolicy));
        context.Store.SaveException = new IOException("save detail");
        SettingsViewModel viewModel = context.CreateViewModel();

        bool changed = await viewModel.SetStartupEnabledAsync(
            isEnabled: true,
            CancellationToken.None);

        Assert.IsFalse(changed);
        Assert.IsTrue(viewModel.IsStartupEnabled);
        Assert.IsFalse(context.Manager.CurrentData.Settings.StartupEnabled);
        StringAssert.Contains(viewModel.InfoBarMessage, "実際の状態");
    }

    [TestMethod]
    public async Task Synchronize_補正保存失敗は実状態と再試行案内を表示する()
    {
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            StartupEnabled = true,
        };
        Context context = await Context.CreateAsync(settings);
        SettingsViewModel viewModel = context.CreateViewModel();

        viewModel.SynchronizeFromCurrentSettings(
            startupStatus: new StartupStatus(StartupState.DisabledByUser),
            isStartupSynchronized: false);

        Assert.IsFalse(viewModel.IsStartupEnabled);
        Assert.IsTrue(viewModel.IsInfoBarOpen);
        StringAssert.Contains(viewModel.InfoBarMessage, "再試行");
    }

    private sealed record Context(
        MemoryDataStore Store,
        GameManager Manager,
        RecordingStartupService StartupService)
    {
        public static async Task<Context> CreateAsync(
            AppSettings? requestedSettings = null)
        {
            AppSettings settings = requestedSettings
                ?? AppSettings.CreateDefault(AppTheme.Light);
            DataEnvelope envelope = new(
                DataEnvelope.CurrentSchemaVersion,
                ImmutableArray<GameEntry>.Empty,
                settings);
            MemoryDataStore store = new(envelope);
            GameManager manager = new(
                store,
                new FakeClock(DateTimeOffset.UtcNow),
                settings);
            await manager.InitializeAsync(envelope, CancellationToken.None);
            return new Context(
                store,
                manager,
                new RecordingStartupService());
        }

        public SettingsViewModel CreateViewModel()
        {
            SettingsViewModel viewModel = new(
                Manager,
                new PassThroughThemeService(),
                new PassThroughBackdropService(),
                StartupService);
            viewModel.MarkReady();
            return viewModel;
        }
    }

    private sealed class RecordingStartupService : IStartupService
    {
        public Queue<StartupChangeResult> Results { get; } = [];

        public List<bool> Requests { get; } = [];

        public Task<StartupStatus> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new StartupStatus(StartupState.Disabled));

        public Task<StartupChangeResult> SetEnabledAsync(
            bool isEnabled,
            CancellationToken cancellationToken)
        {
            Requests.Add(isEnabled);
            return Task.FromResult(Results.Dequeue());
        }
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

    private sealed class MemoryDataStore(DataEnvelope envelope)
        : ILocalDataStore
    {
        public Exception? SaveException { get; set; }

        public int SaveCount { get; private set; }

        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new DataLoadResult(
                    DataLoadStatus.Primary,
                    envelope,
                    "primary",
                    "recovery"));

        public Task SaveAsync(
            DataEnvelope value,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            return SaveException is null
                ? Task.CompletedTask
                : Task.FromException(SaveException);
        }

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
