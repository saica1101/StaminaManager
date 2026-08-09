using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using StaminaManager.Infrastructure.Resources;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.ViewModels;
using System.Collections.Specialized;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class OverviewViewModelTests
{
    private static readonly AppResourceService JapaneseResources = new(
        resourceId => resourceId switch
        {
            "OverviewSaveError" =>
                "データを保存できませんでした。もう一度お試しください。",
            _ => resourceId,
        });

    private static readonly DateTimeOffset NowUtc = new(
        2026,
        7,
        25,
        12,
        0,
        0,
        TimeSpan.Zero);

    [TestMethod]
    public async Task OverviewItems_EmptyCollectionStartsWithAddGameItem()
    {
        FakeClock clock = new(NowUtc);
        ViewModelDataStore store = new();
        GameManager manager = new(
            store,
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        await manager.InitializeAsync(
            manager.CurrentData,
            CancellationToken.None);
        using OverviewViewModel viewModel = CreateViewModel(
            manager,
            clock,
            new RecordingUiDispatcher());

        Assert.HasCount(1, viewModel.OverviewItems);
        Assert.IsInstanceOfType<AddGameItemViewModel>(
            viewModel.OverviewItems[0]);
    }

    [TestMethod]
    public async Task OverviewItems_AppendsAddGameItemAfterRegisteredGames()
    {
        FakeClock clock = new(NowUtc);
        ViewModelDataStore store = new();
        GameManager manager = new(
            store,
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        await manager.InitializeAsync(
            manager.CurrentData,
            CancellationToken.None);
        using OverviewViewModel viewModel = CreateViewModel(
            manager,
            clock,
            new RecordingUiDispatcher());

        await manager.AddAsync(
            new GameDraft(
                "First",
                40,
                100,
                5,
                null,
                RecoverySeconds: 0,
                IsNotificationEnabled: true),
            CancellationToken.None);
        await manager.AddAsync(
            new GameDraft(
                "Second",
                50,
                100,
                5,
                null,
                RecoverySeconds: 0,
                IsNotificationEnabled: true),
            CancellationToken.None);

        Assert.HasCount(3, viewModel.OverviewItems);
        CollectionAssert.AreEqual(
            new[] { "First", "Second" },
            viewModel.OverviewItems
                .OfType<GameCardViewModel>()
                .Select(item => item.Name)
                .ToArray());
        Assert.IsInstanceOfType<AddGameItemViewModel>(
            viewModel.OverviewItems[^1]);
    }

    [TestMethod]
    public async Task GameChanges_UpdateCollectionOnUiDispatcher()
    {
        FakeClock clock = new(NowUtc);
        ViewModelDataStore store = new();
        GameManager manager = new(
            store,
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        await manager.InitializeAsync(
            manager.CurrentData,
            CancellationToken.None);
        RecordingUiDispatcher dispatcher = new();
        using OverviewViewModel viewModel = CreateViewModel(
            manager,
            clock,
            dispatcher);
        bool collectionChangedOnDispatcher = false;
        ((INotifyCollectionChanged)viewModel.Games).CollectionChanged +=
            (_, _) =>
            collectionChangedOnDispatcher = dispatcher.IsExecuting;

        await manager.AddAsync(
            new GameDraft(
                "Game",
                40,
                100,
                5,
                null,
                RecoverySeconds: 0,
                IsNotificationEnabled: true),
            CancellationToken.None);

        Assert.IsTrue(collectionChangedOnDispatcher);
        Assert.HasCount(1, viewModel.Games);
    }

    [TestMethod]
    public async Task LoadingState_DisablesCommandsOnUiDispatcher()
    {
        FakeClock clock = new(NowUtc);
        ViewModelDataStore store = new();
        GameManager manager = new(
            store,
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        RecordingUiDispatcher dispatcher = new();
        using OverviewViewModel viewModel = CreateViewModel(
            manager,
            clock,
            dispatcher);
        bool propertyChangedOnDispatcher = false;
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(OverviewViewModel.IsLoading))
            {
                propertyChangedOnDispatcher = dispatcher.IsExecuting;
            }
        };

        Assert.IsTrue(viewModel.IsLoading);
        Assert.IsFalse(viewModel.AddGameCommand.CanExecute(null));
        Assert.IsFalse(viewModel.EnterCompactModeCommand.CanExecute(null));

        await viewModel.SetLoadingAsync(false);
        Assert.IsTrue(propertyChangedOnDispatcher);
        Assert.IsTrue(viewModel.AddGameCommand.CanExecute(null));

        await viewModel.SetLoadingAsync(true);
        Assert.IsFalse(viewModel.AddGameCommand.CanExecute(null));
    }

    [TestMethod]
    public async Task ShowErrorAsync_DoesNotExposeRawExceptionMessage()
    {
        FakeClock clock = new(NowUtc);
        ViewModelDataStore store = new();
        GameManager manager = new(
            store,
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        RecordingUiDispatcher dispatcher = new();
        using OverviewViewModel viewModel = CreateViewModel(
            manager,
            clock,
            dispatcher);

        await viewModel.ShowErrorAsync(
            new IOException("C:\\private\\data.json failed"));

        Assert.AreEqual(
            "データを保存できませんでした。もう一度お試しください。",
            viewModel.ErrorMessage);
        Assert.DoesNotContain("private", viewModel.ErrorMessage!);
    }

    [TestMethod]
    public async Task UserMessages_UseSharedResourceService()
    {
        FakeClock clock = new(NowUtc);
        GameManager manager = new(
            new ViewModelDataStore(),
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        AppResourceService resources = new(resourceId => $"[{resourceId}]");
        using OverviewViewModel viewModel = new(
            manager,
            clock,
            new RecordingUiDispatcher(),
            resources);

        await viewModel.ShowErrorAsync(new IOException("private"));
        Assert.AreEqual("[OverviewSaveError]", viewModel.ErrorMessage);
        await viewModel.ShowErrorAsync(new OperationCanceledException());
        Assert.AreEqual("[OverviewCanceledError]", viewModel.ErrorMessage);
        await viewModel.ShowErrorAsync(new InvalidOperationException("private"));
        Assert.AreEqual("[OverviewGenericError]", viewModel.ErrorMessage);

        await viewModel.ShowNotificationTargetMissingAsync();
        Assert.AreEqual(
            "[OverviewNotificationTargetMissingError]",
            viewModel.ErrorMessage);
        await viewModel.ShowStartupRecoveryAsync(new StartupRecoveryStatus(
            StartupRecoveryKind.Promoted,
            IsDiagnosticPreserved: true));
        Assert.AreEqual(
            "[OverviewRecoveryDiagnosticPreserved]",
            viewModel.RecoveryMessage);
        await viewModel.ShowStartupRecoveryAsync(new StartupRecoveryStatus(
            StartupRecoveryKind.Promoted,
            IsDiagnosticPreserved: false));
        Assert.AreEqual(
            "[OverviewRecoveryPromoted]",
            viewModel.RecoveryMessage);

        await viewModel.ShowDataLoadWarningAsync(
            DataLoadWarning.SchemaMigrationWritebackFailed);
        Assert.AreEqual(
            "[OverviewSchemaWritebackWarning]",
            viewModel.DataLoadWarningMessage);
    }

    [TestMethod]
    public async Task Constructor_DoesNotSynchronouslyWaitForDispatcher()
    {
        FakeClock clock = new(NowUtc);
        ViewModelDataStore store = new();
        GameManager manager = new(
            store,
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        await manager.InitializeAsync(
            manager.CurrentData,
            CancellationToken.None);
        AlwaysQueuedUiDispatcher dispatcher = new();

        Task<OverviewViewModel> constructionTask = Task.Run(
            () => CreateViewModel(
                manager,
                clock,
                dispatcher));
        Task winner = await Task.WhenAny(
            constructionTask,
            dispatcher.InvocationQueued);
        if (!constructionTask.IsCompleted)
        {
            dispatcher.DrainOne();
        }

        using OverviewViewModel viewModel = await constructionTask;
        Assert.AreSame(constructionTask, winner);
    }

    [TestMethod]
    public async Task Refresh_DoesNotRedispatchOrSynchronouslyWait()
    {
        FakeClock clock = new(NowUtc);
        ViewModelDataStore store = new();
        GameManager manager = new(
            store,
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        await manager.InitializeAsync(
            manager.CurrentData,
            CancellationToken.None);
        await manager.AddAsync(
            new GameDraft(
                "Game",
                40,
                100,
                5,
                null,
                RecoverySeconds: 0,
                IsNotificationEnabled: true),
            CancellationToken.None);
        RecordingUiDispatcher dispatcher = new();
        using OverviewViewModel viewModel = CreateViewModel(
            manager,
            clock,
            dispatcher);
        int invocationCountBeforeRefresh = dispatcher.InvocationCount;

        viewModel.RefreshOnUiThread(NowUtc.AddMinutes(5));

        Assert.AreEqual(
            invocationCountBeforeRefresh,
            dispatcher.InvocationCount);
    }

    private static OverviewViewModel CreateViewModel(
        GameManager manager,
        IClock clock,
        IUiDispatcher dispatcher) => new(
            manager,
            clock,
            dispatcher,
            JapaneseResources);

    private sealed class ViewModelDataStore : ILocalDataStore
    {
        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            DataEnvelope envelope,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
