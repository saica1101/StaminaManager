using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using StaminaManager.Tests.TestDoubles;
using System.Collections.Immutable;

namespace StaminaManager.Tests.Application;

[TestClass]
public sealed class AppCoordinatorNotificationTests
{
    [TestMethod]
    public async Task InitializeAsync_ReconcilesLoadedGamesAndSettings()
    {
        RecordingNotificationReconciler reconciler = new();
        (AppCoordinator coordinator, _) = CreateCoordinator(reconciler);

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.AreEqual(1, reconciler.CallCount);
        Assert.HasCount(1, reconciler.LastGames);
        Assert.IsNotNull(coordinator.LastNotificationReconcileResult);
    }

    [TestMethod]
    public async Task GameMutation_ReconcilesNotificationsAgain()
    {
        RecordingNotificationReconciler reconciler = new();
        (AppCoordinator coordinator, GameManager manager) =
            CreateCoordinator(reconciler);
        await coordinator.InitializeAsync(CancellationToken.None);

        await manager.AddAsync(
            new GameDraft(
                "Second",
                20,
                100,
                5,
                null,
                RecoverySeconds: 0,
                IsNotificationEnabled: true),
            CancellationToken.None);

        Assert.AreEqual(2, reconciler.CallCount);
        Assert.HasCount(2, reconciler.LastGames);
    }

    [TestMethod]
    public async Task InitializeAsync_LanguageMismatchIsFixedBeforeNotifications()
    {
        RecordingNotificationReconciler reconciler = new();
        RecordingLanguageService language = new(reconciler.Operations)
        {
            EffectiveLanguage = AppLanguage.Japanese,
        };
        (AppCoordinator coordinator, _) = CreateCoordinator(
            reconciler,
            language,
            savedLanguage: AppLanguage.English);

        await coordinator.InitializeAsync(CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { AppLanguage.English },
            language.SetRequests);
        Assert.IsLessThan(
            reconciler.Operations.IndexOf("Notifications"),
            reconciler.Operations.IndexOf("LanguageSet"));
        Assert.IsTrue(coordinator.IsLanguageSynchronized);
    }

    [TestMethod]
    public async Task InitializeAsync_LanguageFailureKeepsDataAndRetriesBeforeNotifications()
    {
        RecordingNotificationReconciler reconciler = new();
        RecordingLanguageService language = new(reconciler.Operations)
        {
            EffectiveLanguage = AppLanguage.Japanese,
        };
        language.Results.Enqueue(new LanguageChangeResult(
            AppLanguage.English,
            IsApplied: false,
            LanguageFailureReason.PlatformError));
        language.Results.Enqueue(new LanguageChangeResult(
            AppLanguage.English,
            IsApplied: true,
            LanguageFailureReason.None));
        (AppCoordinator coordinator, GameManager manager) = CreateCoordinator(
            reconciler,
            language,
            savedLanguage: AppLanguage.English);

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.AreEqual(
            AppLanguage.English,
            manager.CurrentData.Settings.Language);
        Assert.IsFalse(coordinator.IsLanguageSynchronized);
        Assert.AreEqual(
            LanguageConsistencyState.Inconsistent,
            coordinator.LanguageConsistencyState);
        Assert.AreEqual(0, reconciler.CallCount);

        await coordinator.ReconcileDerivedStateAsync(CancellationToken.None);

        Assert.IsTrue(coordinator.IsLanguageSynchronized);
        Assert.AreEqual(1, reconciler.CallCount);
        Assert.AreEqual(
            AppLanguage.English,
            manager.CurrentData.Settings.Language);
    }

    [TestMethod]
    public async Task InitializeAsync_MatchingLanguageDoesNotCallSetter()
    {
        RecordingNotificationReconciler reconciler = new();
        RecordingLanguageService language = new(reconciler.Operations)
        {
            EffectiveLanguage = AppLanguage.English,
        };
        (AppCoordinator coordinator, _) = CreateCoordinator(
            reconciler,
            language,
            savedLanguage: AppLanguage.English);

        await coordinator.InitializeAsync(CancellationToken.None);

        Assert.IsEmpty(language.SetRequests);
        Assert.AreEqual(1, reconciler.CallCount);
    }

    private static (AppCoordinator, GameManager) CreateCoordinator(
        INotificationReconciler reconciler,
        IAppLanguageService? language = null,
        AppLanguage savedLanguage = AppLanguage.Japanese)
    {
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            Language = savedLanguage,
        };
        GameEntry game = new(
            Guid.NewGuid(),
            "First",
            BaseStamina: 10,
            MaxStamina: 100,
            RecoveryMinutes: 5,
            RecordedAtUtc: new DateTimeOffset(
                2026,
                7,
                25,
                0,
                0,
                0,
                TimeSpan.Zero),
            ImageAssetId: null,
            SortOrder: 0,
            RecoverySeconds: 0,
            IsNotificationEnabled: true);
        InMemoryDataStore store = new(new DataEnvelope(
            DataEnvelope.CurrentSchemaVersion,
            ImmutableArray.Create(game),
            settings));
        GameManager manager = new(
            store,
            new FakeClock(game.RecordedAtUtc),
            settings);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher(),
            new PassThroughThemeService(),
            new PassThroughBackdropService(),
            new PassThroughStartupService(),
            new PassThroughWindowStateService(),
            reconciler,
            appLanguageService: language,
            sessionLanguage: AppLanguage.Japanese);
        return (coordinator, manager);
    }

    private sealed class RecordingNotificationReconciler
        : INotificationReconciler
    {
        public int CallCount { get; private set; }

        public List<string> Operations { get; } = [];

        public IReadOnlyCollection<GameEntry> LastGames { get; private set; } =
            Array.Empty<GameEntry>();

        public Task<NotificationReconcileResult> ReconcileAsync(
            IReadOnlyCollection<GameEntry> games,
            AppSettings settings,
            CancellationToken cancellationToken)
        {
            Operations.Add("Notifications");
            CallCount++;
            LastGames = games.ToArray();
            return Task.FromResult(NotificationReconcileResult.Success);
        }
    }

    private sealed class RecordingLanguageService(
        List<string> operations) : IAppLanguageService
    {
        public AppLanguage EffectiveLanguage { get; set; }

        public List<AppLanguage> SetRequests { get; } = [];

        public Queue<LanguageChangeResult> Results { get; } = [];

        public AppLanguage GetEffectiveLanguage() => EffectiveLanguage;

        public LanguageChangeResult SetLanguage(AppLanguage language)
        {
            operations.Add("LanguageSet");
            SetRequests.Add(language);
            LanguageChangeResult result = Results.Count > 0
                ? Results.Dequeue()
                : new LanguageChangeResult(
                    language,
                    IsApplied: true,
                    LanguageFailureReason.None);
            if (result.IsApplied)
            {
                EffectiveLanguage = language;
            }

            return result;
        }
    }

    private sealed class InMemoryDataStore(DataEnvelope data)
        : ILocalDataStore
    {
        private DataEnvelope _data = data;

        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) => Task.FromResult(
                new DataLoadResult(
                    DataLoadStatus.Primary,
                    _data,
                    "data.json",
                    "data.recovery.json"));

        public Task SaveAsync(
            DataEnvelope envelope,
            CancellationToken cancellationToken)
        {
            _data = envelope;
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
            CancellationToken cancellationToken) => throw new
                NotSupportedException();
    }

    private sealed class PassThroughWindowStateService
        : IWindowStateService
    {
        public AppDisplayMode CurrentDisplayMode => AppDisplayMode.Standard;

        public void ApplyDisplayMode(AppDisplayMode displayMode)
        {
        }

        public void CaptureCurrent()
        {
        }
    }
}
