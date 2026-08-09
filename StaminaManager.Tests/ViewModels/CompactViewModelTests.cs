using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using StaminaManager.Infrastructure.Resources;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.ViewModels;
using System.Collections.Immutable;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class CompactViewModelTests
{
    private static readonly DateTimeOffset NowUtc = new(
        2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task EmptyGames_ShowsEmptyAddState()
    {
        Context context = await CreateAsync();
        using CompactViewModel viewModel = context.CreateViewModel();

        Assert.IsTrue(viewModel.IsEmpty);
        Assert.IsFalse(viewModel.HasSelectedGame);
        Assert.IsNull(viewModel.SelectedGame);
        Assert.IsTrue(viewModel.AddGameCommand.CanExecute(null));
    }

    [TestMethod]
    [DataRow(StaminaStatus.Safe, "localized-safe")]
    [DataRow(StaminaStatus.Attention, "localized-attention")]
    [DataRow(StaminaStatus.NearFull, "localized-near-full")]
    [DataRow(StaminaStatus.Full, "localized-full")]
    [DataRow(StaminaStatus.OverCap, "localized-over-cap")]
    public async Task SelectedStatusText_UsesLocalizedResource(
        StaminaStatus status,
        string expected)
    {
        Context context = await CreateAsync(CreateEntry("Localized", 0));
        using CompactViewModel viewModel = context.CreateViewModel();
        viewModel.SelectedGame!.Status = status;

        Assert.AreEqual(expected, viewModel.SelectedStatusText);
    }

    [TestMethod]
    [DataRow(2, 900, "満タンまで 00:00:02")]
    [DataRow(3_723, 0, "満タンまで 01:02:03")]
    [DataRow(86_400, 900, "満タンまで 24:00:00")]
    public async Task SelectedRemainingText_FormatsSubdayDuration(
        int recoveryIntervalSeconds,
        int elapsedMilliseconds,
        string expected)
    {
        GameEntry game = CreateRemainingEntry(
            recoveryIntervalSeconds,
            TimeSpan.FromMilliseconds(elapsedMilliseconds));
        Context context = await CreateAsync(game);
        using CompactViewModel viewModel = context.CreateViewModel();

        Assert.AreEqual(expected, viewModel.SelectedRemainingText);
    }

    [TestMethod]
    public async Task SelectedRemainingText_UsesDayFormatAtExactlyOneDay()
    {
        GameEntry game = CreateRemainingEntry(
            86_400,
            TimeSpan.Zero);
        Context context = await CreateAsync(game);
        using CompactViewModel viewModel = context.CreateViewModel();

        Assert.AreEqual(
            "満タンまで 1日 00:00",
            viewModel.SelectedRemainingText);
    }

    [TestMethod]
    public async Task FirstAddedGame_SynchronizesPersistedSelectionOnce()
    {
        Context context = await CreateAsync();
        using CompactViewModel viewModel = context.CreateViewModel();

        GameEntry added = await context.Manager.AddAsync(
            new GameDraft(
                "First",
                CurrentStamina: 40,
                MaxStamina: 100,
                RecoveryMinutes: 5,
                ImageAssetId: null,
                RecoverySeconds: 0,
                IsNotificationEnabled: true),
            CancellationToken.None);

        Assert.AreEqual(1, context.Store.SaveCount);
        Assert.AreEqual(added.Id, viewModel.SelectedGame!.Id);
        Assert.AreEqual(
            added.Id,
            context.Manager.CurrentData.Settings.SelectedCompactGameId);
    }

    [TestMethod]
    public async Task DeletedSelectedGame_FallsBackToFirstRegisteredGame()
    {
        GameEntry first = CreateEntry("First", 0);
        GameEntry selected = CreateEntry("Selected", 1);
        Context context = await CreateAsyncWithSelection(
            selected.Id,
            first,
            selected);
        using CompactViewModel viewModel = context.CreateViewModel();
        int saveCountBeforeDelete = context.Store.SaveCount;

        await context.Manager.DeleteAsync(
            selected.Id,
            CancellationToken.None);

        Assert.IsNotNull(viewModel.SelectedGame);
        Assert.AreEqual(first.Id, viewModel.SelectedGame.Id);
        Assert.AreEqual(
            first.Id,
            context.Manager.CurrentData.Settings.SelectedCompactGameId);
        Assert.AreEqual(
            saveCountBeforeDelete + 1,
            context.Store.SaveCount);
    }

    [TestMethod]
    public async Task SelectionPersistenceFailure_DoesNotChangeSelection()
    {
        GameEntry first = CreateEntry("First", 0);
        GameEntry second = CreateEntry("Second", 1);
        Context context = await CreateAsync(first, second);
        using CompactViewModel viewModel = context.CreateViewModel();
        context.Store.SaveException = new IOException("failure");

        await Assert.ThrowsExactlyAsync<IOException>(
            () => viewModel.SelectGameAsync(
                second.Id,
                CancellationToken.None));

        Assert.AreEqual(first.Id, viewModel.SelectedGame!.Id);
        Assert.AreEqual(
            first.Id,
            context.Manager.CurrentData.Settings.SelectedCompactGameId);
        Assert.AreEqual(
            "localized-selection-save-error",
            viewModel.ErrorMessage);
    }

    [TestMethod]
    public async Task EditingSelectedGame_RaisesSelectionChangedForControlResync()
    {
        GameEntry game = CreateEntry("Original", 0);
        Context context = await CreateAsync(game);
        using CompactViewModel viewModel = context.CreateViewModel();
        List<string?> changedProperties = [];
        viewModel.PropertyChanged += (_, args) =>
            changedProperties.Add(args.PropertyName);
        GameDraft original = new(
            game.Name,
            game.BaseStamina,
            game.MaxStamina,
            game.RecoveryMinutes,
            game.ImageAssetId,
            RecoverySeconds: 0,
            IsNotificationEnabled: true);

        await context.Manager.EditAsync(
            game.Id,
            original,
            original with { Name = "Renamed" },
            CancellationToken.None);

        CollectionAssert.Contains(
            changedProperties,
            nameof(CompactViewModel.SelectedGame));
        Assert.AreEqual("Renamed", viewModel.SelectedGame!.Name);
    }

    private static async Task<Context> CreateAsync(
        params GameEntry[] games) => await CreateAsyncWithSelection(
            games.FirstOrDefault()?.Id,
            games);

    private static async Task<Context> CreateAsyncWithSelection(
        Guid? selectedGameId,
        params GameEntry[] games)
    {
        AppSettings settings = AppSettings.CreateDefault(AppTheme.Light) with
        {
            LastDisplayMode = AppDisplayMode.Standard,
            SelectedCompactGameId = selectedGameId,
        };
        DataEnvelope envelope = new(
            DataEnvelope.CurrentSchemaVersion,
            games.ToImmutableArray(),
            settings);
        MemoryDataStore store = new(envelope);
        FakeClock clock = new(NowUtc);
        GameManager manager = new(store, clock, settings);
        AppCoordinator coordinator = new(
            store,
            manager,
            new RecordingUiDispatcher());
        await coordinator.InitializeAsync(CancellationToken.None);
        return new Context(store, manager, coordinator, clock);
    }

    private static GameEntry CreateEntry(string name, int sortOrder) => new(
        Guid.NewGuid(),
        name,
        BaseStamina: 40,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        RecordedAtUtc: NowUtc,
        ImageAssetId: null,
        sortOrder,
        RecoverySeconds: 0,
        IsNotificationEnabled: true);

    private static GameEntry CreateRemainingEntry(
        int recoveryIntervalSeconds,
        TimeSpan elapsed) => new(
            Guid.NewGuid(),
            "Remaining",
            BaseStamina: 0,
            MaxStamina: 1,
            RecoveryMinutes: recoveryIntervalSeconds / 60,
            RecordedAtUtc: NowUtc - elapsed,
            ImageAssetId: null,
            SortOrder: 0,
            RecoverySeconds: recoveryIntervalSeconds % 60,
            IsNotificationEnabled: true);

    private sealed record Context(
        MemoryDataStore Store,
        GameManager Manager,
        AppCoordinator Coordinator,
        FakeClock Clock)
    {
        public CompactViewModel CreateViewModel() => new(
            Manager,
            Clock,
            Coordinator,
            new RecordingUiDispatcher(),
            new AppResourceService(ResolveString));

        private static string ResolveString(string resourceId) =>
            resourceId switch
            {
                "StaminaStatusSafe" => "localized-safe",
                "StaminaStatusAttention" => "localized-attention",
                "StaminaStatusNearFull" => "localized-near-full",
                "StaminaStatusFull" => "localized-full",
                "StaminaStatusOverCap" => "localized-over-cap",
                "RemainingTimeFull" => "満タン",
                "RemainingTimeDaysFormat" =>
                    "満タンまで {0}日 {1:00}:{2:00}",
                "RemainingTimeHoursSecondsFormat" =>
                    "満タンまで {0:00}:{1:00}:{2:00}",
                "CompactSelectionSaveError" =>
                    "localized-selection-save-error",
                _ => throw new ArgumentOutOfRangeException(
                    nameof(resourceId)),
            };
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
            cancellationToken.ThrowIfCancellationRequested();
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
