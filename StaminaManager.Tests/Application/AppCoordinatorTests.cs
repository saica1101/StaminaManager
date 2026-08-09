using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using StaminaManager.Infrastructure.Resources;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.ViewModels;
using System.Collections.Immutable;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class AppCoordinatorTests
{
    private static readonly AppResourceService OverviewResources = new(
        resourceId => resourceId);

    private static readonly DateTimeOffset NowUtc = new(
        2026,
        7,
        25,
        12,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public async Task InitializeForLaunchAsync_旧背景の保存失敗をOverviewで案内する()
    {
        CoordinatorDataStore store = new(new DataLoadResult(
            DataLoadStatus.Primary,
            CreateEnvelope("Loaded"),
            "private-primary-path",
            "private-recovery-path",
            DataLoadWarning.SchemaMigrationWritebackFailed));
        GameManager manager = CreateManager(store);
        RecordingUiDispatcher dispatcher = new();
        AppCoordinator coordinator = new(store, manager, dispatcher);
        OverviewViewModel overview = CreateOverview(manager, dispatcher);

        await global::StaminaManager.App.InitializeForLaunchAsync(
            coordinator,
            overview,
            CancellationToken.None);

        Assert.IsTrue(overview.IsDataLoadWarningInfoBarOpen);
        Assert.AreEqual(
            "OverviewSchemaWritebackWarning",
            overview.DataLoadWarningMessage);
        Assert.DoesNotContain("private-", overview.DataLoadWarningMessage);
    }

    [TestMethod]
    public async Task InitializeForLaunchAsync_Recoveryを恒久昇格して案内後も保存できる()
    {
        DataEnvelope recovered = CreateEnvelope("Recovered");
        CoordinatorDataStore store = new(new DataLoadResult(
            DataLoadStatus.Recovery,
            recovered,
            "private-primary-path",
            "private-recovery-path"))
        {
            PromotionResult = new RecoveryPromotionResult(
                recovered,
                "private-primary-path",
                "private-recovery-path",
                "private-diagnostic-path"),
        };
        GameManager manager = CreateManager(store);
        RecordingUiDispatcher dispatcher = new();
        AppCoordinator coordinator = new(store, manager, dispatcher);
        OverviewViewModel overview = CreateOverview(manager, dispatcher);

        await global::StaminaManager.App.InitializeForLaunchAsync(
            coordinator,
            overview,
            CancellationToken.None);

        Assert.AreEqual(1, store.PromoteCount);
        Assert.IsTrue(coordinator.IsInitialized);
        Assert.AreEqual(
            StartupRecoveryKind.Promoted,
            coordinator.StartupRecovery.Kind);
        Assert.IsTrue(coordinator.StartupRecovery.IsDiagnosticPreserved);
        Assert.IsTrue(overview.IsRecoveryInfoBarOpen);
        Assert.AreEqual(
            "OverviewRecoveryDiagnosticPreserved",
            overview.RecoveryMessage);
        Assert.IsFalse(overview.RecoveryMessage.Contains(
            "private-",
            StringComparison.Ordinal));

        int saveCountBeforeAdd = store.SaveCount;
        await manager.AddAsync(
            new GameDraft(
                "After recovery",
                1,
                100,
                5,
                null,
                RecoverySeconds: 0,
                IsNotificationEnabled: true),
            CancellationToken.None);
        Assert.AreEqual(saveCountBeforeAdd + 1, store.SaveCount);
    }

    [TestMethod]
    public async Task InitializeForLaunchAsync_Recovery昇格失敗は起動失敗として伝播する()
    {
        CoordinatorDataStore store = new(new DataLoadResult(
            DataLoadStatus.Recovery,
            CreateEnvelope("Recovered"),
            "primary",
            "recovery"))
        {
            PromotionException = new IOException("private path detail"),
        };
        GameManager manager = CreateManager(store);
        RecordingUiDispatcher dispatcher = new();
        AppCoordinator coordinator = new(store, manager, dispatcher);
        OverviewViewModel overview = CreateOverview(manager, dispatcher);

        await Assert.ThrowsExactlyAsync<IOException>(
            () => global::StaminaManager.App.InitializeForLaunchAsync(
                coordinator,
                overview,
                CancellationToken.None));

        Assert.IsFalse(coordinator.IsInitialized);
        Assert.IsTrue(overview.IsLoading);
        Assert.IsFalse(overview.IsRecoveryInfoBarOpen);
    }

    [TestMethod]
    public async Task InitializeAsync_Recovery昇格失敗後の再試行でも恒久昇格する()
    {
        DataEnvelope recovered = CreateEnvelope("Recovered");
        CoordinatorDataStore store = new(new DataLoadResult(
            DataLoadStatus.Recovery,
            recovered,
            "primary",
            "recovery"))
        {
            PromotionResult = new RecoveryPromotionResult(
                recovered,
                "primary",
                "recovery",
                "diagnostic"),
            PromotionException = new IOException("temporary failure"),
        };
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher());

        await Assert.ThrowsExactlyAsync<IOException>(
            () => coordinator.InitializeAsync(CancellationToken.None));

        store.PromotionException = null;
        DataLoadResult result = await coordinator.InitializeAsync(
            CancellationToken.None);

        Assert.AreEqual(2, store.PromoteCount);
        Assert.AreEqual(DataLoadStatus.Recovery, result.Status);
        Assert.AreEqual(DataLoadStatus.Primary, coordinator.LastLoadResult?.Status);
        Assert.AreEqual(
            StartupRecoveryKind.Promoted,
            coordinator.StartupRecovery.Kind);
        Assert.IsTrue(coordinator.IsInitialized);
    }

    [TestMethod]
    public async Task InitializeAsync_SetsInitializedOnlyAfterReconciliation()
    {
        CoordinatorDataStore store = new(CreateLoadResult("Loaded"));
        GameManager manager = CreateManager(store);
        RecordingUiDispatcher dispatcher = new();
        AppCoordinator coordinator = new(store, manager, dispatcher);
        using CancellationTokenSource cancellation = new();
        EventHandler<AppNavigationRequest> cancelDuringNavigation = (_, _) =>
            cancellation.Cancel();
        coordinator.NavigationRequested += cancelDuringNavigation;

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => coordinator.InitializeAsync(cancellation.Token));

        Assert.IsFalse(coordinator.IsInitialized);
        coordinator.NavigationRequested -= cancelDuringNavigation;
        await coordinator.InitializeAsync(CancellationToken.None);
        Assert.IsTrue(coordinator.IsInitialized);
        Assert.AreEqual(1, store.LoadCount);
    }

    [TestMethod]
    public async Task PromoteRecoveryAsync_BeforeInitializationIsRejected()
    {
        CoordinatorDataStore store = new(CreateLoadResult("Loaded"))
        {
            PromotionResult = CreatePromotionResult("Recovered"),
        };
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => coordinator.PromoteRecoveryAsync(
                CancellationToken.None));

        Assert.AreEqual(0, store.PromoteCount);
    }

    [TestMethod]
    public async Task PromoteRecoveryAsync_UsesGameManagerAtomicPromotion()
    {
        CoordinatorDataStore store = new(CreateLoadResult("Loaded"))
        {
            PromotionResult = CreatePromotionResult("Recovered"),
        };
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher());
        await coordinator.InitializeAsync(CancellationToken.None);

        await coordinator.PromoteRecoveryAsync(CancellationToken.None);

        Assert.AreEqual(1, store.PromoteCount);
        Assert.AreEqual("Recovered", manager.Games.Single().Name);
    }

    [TestMethod]
    public async Task InitializeAsync_EmptyLoadInitializesEmptyGameManager()
    {
        CoordinatorDataStore store = new(new DataLoadResult(
            DataLoadStatus.Empty,
            Envelope: null,
            "primary",
            "recovery"));
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher());

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(manager.IsInitialized);
        Assert.IsEmpty(manager.Games);
    }

    [TestMethod]
    public async Task InitializeAsync_SuccessfulSecondCallIsRejected()
    {
        CoordinatorDataStore store = new(CreateLoadResult("Loaded"));
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher());
        await coordinator.InitializeAsync(CancellationToken.None);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => coordinator.InitializeAsync(CancellationToken.None));

        Assert.AreEqual(1, store.LoadCount);
    }

    [TestMethod]
    public async Task InitializeAsync_ConcurrentSecondCallIsRejected()
    {
        CoordinatorDataStore store = new(CreateLoadResult("Loaded"))
        {
            ShouldBlockLoad = true,
        };
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher());
        Task<DataLoadResult> first = coordinator.InitializeAsync(
            CancellationToken.None);
        await store.LoadStarted;
        Task<DataLoadResult> second = coordinator.InitializeAsync(
            CancellationToken.None);

        store.ReleaseLoad();
        await first;
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => second);

        Assert.AreEqual(1, store.LoadCount);
    }

    [TestMethod]
    public async Task RouteActivationAsync_RaisesNavigationOnUiDispatcher()
    {
        CoordinatorDataStore store = new(CreateLoadResult("Loaded"));
        GameManager manager = CreateManager(store);
        RecordingUiDispatcher dispatcher = new();
        AppCoordinator coordinator = new(store, manager, dispatcher);
        ShellViewModel shell = new();
        bool raisedOnDispatcher = false;
        coordinator.NavigationRequested += (_, request) =>
        {
            raisedOnDispatcher = dispatcher.IsExecuting;
            shell.ApplyNavigationRequest(request);
        };
        Guid gameId = Guid.NewGuid();

        await Task.Run(() => coordinator.RouteActivationAsync(gameId));

        Assert.IsTrue(raisedOnDispatcher);
        Assert.AreEqual(gameId, shell.SelectedGameId);
        Assert.AreEqual(AppPage.Overview, shell.CurrentPage);
    }

    [TestMethod]
    public async Task InitializeAsync_RestoresSavedCompactModeAfterDataLoad()
    {
        Guid savedGameId = Guid.NewGuid();
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            LastDisplayMode = AppDisplayMode.Compact,
            SelectedCompactGameId = savedGameId,
        };
        CoordinatorDataStore store = new(new DataLoadResult(
            DataLoadStatus.Primary,
            CreateEnvelope(settings, CreateEntry(savedGameId, "Saved", 0)),
            "primary",
            "recovery"));
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher());
        AppNavigationRequest? request = null;
        coordinator.NavigationRequested += (_, value) => request = value;

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.IsNotNull(request);
        Assert.AreEqual(AppDisplayMode.Compact, request.DisplayMode);
        Assert.AreEqual(savedGameId, request.GameId);
    }

    [TestMethod]
    public async Task InitializeAsync_MissingCompactSelectionFallsBackToFirstGame()
    {
        GameEntry first = CreateEntry(Guid.NewGuid(), "First", 0);
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            LastDisplayMode = AppDisplayMode.Compact,
            SelectedCompactGameId = Guid.NewGuid(),
        };
        CoordinatorDataStore store = new(new DataLoadResult(
            DataLoadStatus.Primary,
            CreateEnvelope(settings, first),
            "primary",
            "recovery"));
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher());
        AppNavigationRequest? request = null;
        coordinator.NavigationRequested += (_, value) => request = value;

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.AreEqual(AppDisplayMode.Compact, request!.DisplayMode);
        Assert.AreEqual(first.Id, request.GameId);
    }

    [TestMethod]
    public async Task ChangeDisplayModeAsync_PersistsBeforeNavigation()
    {
        CoordinatorDataStore store = new(CreateLoadResult("Loaded"));
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher());
        await coordinator.InitializeAsync(CancellationToken.None);
        AppNavigationRequest? request = null;
        coordinator.NavigationRequested += (_, value) => request = value;

        await coordinator.ChangeDisplayModeAsync(
            AppDisplayMode.Compact,
            manager.Games[0].Id,
            CancellationToken.None);

        Assert.AreEqual(
            AppDisplayMode.Compact,
            manager.CurrentData.Settings.LastDisplayMode);
        Assert.AreEqual(manager.Games[0].Id, request!.GameId);
    }

    [TestMethod]
    public async Task SelectCompactGameAsync_PreservesCurrentDisplayMode()
    {
        GameEntry first = CreateEntry(Guid.NewGuid(), "First", 0);
        GameEntry second = CreateEntry(Guid.NewGuid(), "Second", 1);
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light);
        CoordinatorDataStore store = new(new DataLoadResult(
            DataLoadStatus.Primary,
            CreateEnvelope(settings, first, second),
            "primary",
            "recovery"));
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher());
        await coordinator.InitializeAsync(CancellationToken.None);
        AppNavigationRequest? request = null;
        coordinator.NavigationRequested += (_, value) => request = value;

        await coordinator.SelectCompactGameAsync(
            second.Id,
            CancellationToken.None);

        Assert.AreEqual(
            AppDisplayMode.Standard,
            manager.CurrentData.Settings.LastDisplayMode);
        Assert.AreEqual(AppDisplayMode.Standard, request!.DisplayMode);
        Assert.AreEqual(second.Id, request.GameId);
    }

    [TestMethod]
    public async Task InitializeAsync_BackdropFallbackKeepsSavedPreference()
    {
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Dark) with
        {
            Backdrop = BackdropKind.Acrylic,
            AcrylicTintOpacityPercent = 55,
        };
        CoordinatorDataStore store = new(new DataLoadResult(
            DataLoadStatus.Primary,
            CreateEnvelope(settings),
            "primary",
            "recovery"));
        GameManager manager = CreateManager(store);
        RecordingUiDispatcher dispatcher = new();
        RecordingThemeService themeService = new(
            () => dispatcher.IsExecuting);
        RecordingBackdropService backdropService = new()
        {
            NextResult = new BackdropResult(
                BackdropKind.Acrylic,
                BackdropKind.Solid,
                BackdropFallbackReason.TransparencyDisabled,
                "透明効果が無効です。"),
        };
        AppCoordinator coordinator = new(
            store,
            manager,
            dispatcher,
            themeService,
            backdropService);

        await coordinator.InitializeAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { AppTheme.Dark },
            themeService.Requests);
        CollectionAssert.AreEqual(
            new[] { BackdropKind.Acrylic },
            backdropService.Requests);
        Assert.AreEqual(
            new BackdropRequest(BackdropKind.Acrylic, 55),
            backdropService.RequestModels.Single());
        Assert.AreEqual(
            BackdropKind.Acrylic,
            manager.CurrentData.Settings.Backdrop);
        Assert.AreEqual(
            BackdropKind.Solid,
            coordinator.LastBackdropResult!.ActualBackdrop);
        Assert.AreEqual(0, store.SaveCount);
        Assert.IsTrue(themeService.WasAppliedOnUiDispatcher);
    }

    [TestMethod]
    [DataRow(true, StartupState.DisabledByUser, false)]
    [DataRow(false, StartupState.EnabledByPolicy, true)]
    public async Task InitializeAsync_Startup実状態へ保存設定を補正する(
        bool savedEnabled,
        StartupState expectedState,
        bool expectedEnabled)
    {
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            StartupEnabled = savedEnabled,
        };
        CoordinatorDataStore store = new(new DataLoadResult(
            DataLoadStatus.Primary,
            CreateEnvelope(settings),
            "primary",
            "recovery"));
        GameManager manager = CreateManager(store);
        RecordingStartupService startupService = new(expectedState);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher(),
            new RecordingThemeService(() => true),
            new RecordingBackdropService(),
            startupService);

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.AreEqual(
            expectedEnabled,
            manager.CurrentData.Settings.StartupEnabled);
        Assert.AreEqual(1, store.SaveCount);
        Assert.AreEqual(expectedState, coordinator.LastStartupStatus!.State);
    }

    [TestMethod]
    public async Task InitializeAsync_Startup補正保存失敗でも本体初期化を維持する()
    {
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            StartupEnabled = true,
        };
        CoordinatorDataStore store = new(new DataLoadResult(
            DataLoadStatus.Primary,
            CreateEnvelope(settings),
            "primary",
            "recovery"))
        {
            SaveException = new IOException("save detail"),
        };
        GameManager manager = CreateManager(store);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher(),
            new RecordingThemeService(() => true),
            new RecordingBackdropService(),
            new RecordingStartupService(StartupState.DisabledByUser));

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(coordinator.IsInitialized);
        Assert.IsTrue(manager.CurrentData.Settings.StartupEnabled);
        Assert.AreEqual(
            StartupState.DisabledByUser,
            coordinator.LastStartupStatus!.State);
        Assert.IsFalse(coordinator.IsStartupSynchronized);
        Assert.AreEqual(
            StartupFailureReason.OperationFailed,
            coordinator.StartupReconcileFailureReason);
    }

    private static GameManager CreateManager(ILocalDataStore store) => new(
        store,
        new FakeClock(NowUtc),
        AppSettings.CreateDefault(AppTheme.Light));

    private static OverviewViewModel CreateOverview(
        GameManager manager,
        IUiDispatcher dispatcher) => new(
            manager,
            new FakeClock(NowUtc),
            dispatcher,
            OverviewResources);

    private static DataLoadResult CreateLoadResult(string gameName) => new(
        DataLoadStatus.Primary,
        CreateEnvelope(gameName),
        "primary",
        "recovery");

    private static RecoveryPromotionResult CreatePromotionResult(
        string gameName) => new(
            CreateEnvelope(gameName),
            "primary",
            "recovery",
            DiagnosticBackupPath: null);

    private static DataEnvelope CreateEnvelope(string gameName) => new(
        DataEnvelope.CurrentSchemaVersion,
        ImmutableArray.Create(new GameEntry(
            Guid.NewGuid(),
            gameName,
            BaseStamina: 40,
            MaxStamina: 100,
            RecoveryMinutes: 5,
            RecordedAtUtc: NowUtc,
            ImageAssetId: null,
            SortOrder: 0,
            RecoverySeconds: 0,
            IsNotificationEnabled: true)),
        AppSettings.CreateDefault(AppTheme.Light));

    private static DataEnvelope CreateEnvelope(
        AppSettings settings,
        params GameEntry[] games) => new(
        DataEnvelope.CurrentSchemaVersion,
        games.ToImmutableArray(),
        settings);

    private static GameEntry CreateEntry(
        Guid id,
        string name,
        int sortOrder) => new(
        id,
        name,
        BaseStamina: 40,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        RecordedAtUtc: NowUtc,
        ImageAssetId: null,
        sortOrder,
        RecoverySeconds: 0,
        IsNotificationEnabled: true);

    private sealed class CoordinatorDataStore(DataLoadResult loadResult)
        : ILocalDataStore
    {
        private readonly TaskCompletionSource _loadStarted = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _continueLoad = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public RecoveryPromotionResult? PromotionResult { get; init; }

        public Exception? PromotionException { get; set; }

        public int PromoteCount { get; private set; }

        public int LoadCount { get; private set; }

        public int SaveCount { get; private set; }

        public bool ShouldBlockLoad { get; init; }

        public Exception? SaveException { get; set; }

        public Task LoadStarted => _loadStarted.Task;

        public async Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken)
        {
            LoadCount++;
            if (ShouldBlockLoad)
            {
                _loadStarted.TrySetResult();
                await _continueLoad.Task.WaitAsync(cancellationToken);
            }

            return loadResult;
        }

        public Task SaveAsync(
            DataEnvelope envelope,
            CancellationToken cancellationToken)
        {
            SaveCount++;
            return SaveException is null
                ? Task.CompletedTask
                : Task.FromException(SaveException);
        }

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken)
        {
            PromoteCount++;
            if (PromotionException is not null)
            {
                return Task.FromException<RecoveryPromotionResult>(
                    PromotionException);
            }

            return Task.FromResult(
                PromotionResult ?? throw new NotSupportedException());
        }

        public void ReleaseLoad() => _continueLoad.TrySetResult();
    }

    private sealed class RecordingThemeService(
        Func<bool> isOnUiDispatcher) : IThemeService
    {
        public List<AppTheme> Requests { get; } = [];

        public bool WasAppliedOnUiDispatcher { get; private set; }

        public AppTheme ResolveInitialTheme() => AppTheme.Light;

        public ThemeResult Apply(AppTheme requestedTheme)
        {
            WasAppliedOnUiDispatcher = isOnUiDispatcher();
            Requests.Add(requestedTheme);
            return new ThemeResult(
                requestedTheme,
                requestedTheme,
                IsApplied: true,
                ErrorMessage: null);
        }
    }

    private sealed class RecordingBackdropService : IBackdropService
    {
        public List<BackdropKind> Requests { get; } = [];

        public List<BackdropRequest> RequestModels { get; } = [];

        public BackdropResult? NextResult { get; init; }

        public BackdropResult Apply(BackdropRequest request)
        {
            Requests.Add(request.Kind);
            RequestModels.Add(request);
            return NextResult ?? new BackdropResult(
                request.Kind,
                request.Kind,
                BackdropFallbackReason.None,
                ErrorMessage: null,
                request.Kind == BackdropKind.Acrylic
                    ? request.AcrylicTintOpacityPercent
                    : null);
        }
    }

    private sealed class RecordingStartupService(StartupState state)
        : IStartupService
    {
        public Task<StartupStatus> GetStatusAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new StartupStatus(state));

        public Task<StartupChangeResult> SetEnabledAsync(
            bool isEnabled,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
