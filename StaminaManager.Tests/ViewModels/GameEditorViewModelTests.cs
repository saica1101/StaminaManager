using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Models;
using StaminaManager.Core.Persistence;
using StaminaManager.Core.Validation;
using StaminaManager.Tests.TestDoubles;
using StaminaManager.ViewModels;
using System.Collections.Immutable;

namespace StaminaManager.Tests.ViewModels;

[TestClass]
public sealed class GameEditorViewModelTests
{
    private static readonly DateTimeOffset NowUtc = new(
        2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public async Task InvalidFields_ShowInlineErrorsAndDisableSave()
    {
        (GameEditorViewModel viewModel, _, _) =
            await CreateForAddAsync();

        viewModel.Name = " ";
        viewModel.CurrentStamina = double.NaN;
        viewModel.MaxStamina = 0;
        viewModel.RecoveryMinutes = 525_601;

        Assert.IsFalse(viewModel.CanSave);
        Assert.IsNotNull(viewModel.NameError);
        Assert.IsNotNull(viewModel.CurrentStaminaError);
        Assert.IsNotNull(viewModel.MaxStaminaError);
        Assert.IsNotNull(viewModel.RecoveryMinutesError);

        viewModel.Name = "Game";
        viewModel.CurrentStamina = 40;
        viewModel.MaxStamina = 100;
        viewModel.RecoveryMinutes = 5;

        Assert.IsTrue(viewModel.CanSave);
        Assert.IsNull(viewModel.NameError);
        Assert.IsNull(viewModel.CurrentStaminaError);
        Assert.IsNull(viewModel.MaxStaminaError);
        Assert.IsNull(viewModel.RecoveryMinutesError);
    }

    [TestMethod]
    public async Task ActionText_UsesAddForNewAndSaveForEdit()
    {
        (GameEditorViewModel addViewModel, _, _) =
            await CreateForAddAsync();
        (GameEditorViewModel editViewModel, _, _) =
            await CreateForEditAsync(CreateEntry());

        Assert.AreEqual("追加", addViewModel.ActionText);
        Assert.AreEqual("保存", editViewModel.ActionText);
    }

    [TestMethod]
    public async Task NewEditor_UsesDefaultRecoveryAndNotificationValues()
    {
        (GameEditorViewModel viewModel, _, _) =
            await CreateForAddAsync();

        Assert.AreEqual(8d, viewModel.RecoveryMinutes);
        Assert.AreEqual(0d, viewModel.RecoverySeconds);
        Assert.IsTrue(viewModel.IsNotificationEnabled);
    }

    [TestMethod]
    public async Task EditEditor_RestoresRecoveryAndNotificationValues()
    {
        GameEntry entry = CreateEntry() with
        {
            RecoveryMinutes = 7,
            RecoverySeconds = 45,
            IsNotificationEnabled = false,
        };

        (GameEditorViewModel viewModel, _, _) =
            await CreateForEditAsync(entry);

        Assert.AreEqual(7d, viewModel.RecoveryMinutes);
        Assert.AreEqual(45d, viewModel.RecoverySeconds);
        Assert.IsFalse(viewModel.IsNotificationEnabled);
    }

    [TestMethod]
    [DataRow(1.5)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(-1d)]
    [DataRow(525_601d)]
    public async Task InvalidRecoveryMinutes_ShowFieldErrorAndDisableSave(
        double recoveryMinutes)
    {
        (GameEditorViewModel viewModel, _, _) =
            await CreateForAddAsync();
        viewModel.Name = "Game";

        viewModel.RecoveryMinutes = recoveryMinutes;

        Assert.IsNotNull(viewModel.RecoveryMinutesError);
        Assert.IsNull(viewModel.RecoveryIntervalError);
        Assert.IsFalse(viewModel.CanSave);
    }

    [TestMethod]
    [DataRow(1.5)]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(-1d)]
    [DataRow(60d)]
    public async Task InvalidRecoverySeconds_ShowFieldErrorAndDisableSave(
        double recoverySeconds)
    {
        (GameEditorViewModel viewModel, _, _) =
            await CreateForAddAsync();
        viewModel.Name = "Game";

        viewModel.RecoverySeconds = recoverySeconds;

        Assert.IsNotNull(viewModel.RecoverySecondsError);
        Assert.IsNull(viewModel.RecoveryIntervalError);
        Assert.IsFalse(viewModel.CanSave);
    }

    [TestMethod]
    [DataRow(0d, 0d)]
    [DataRow(525_600d, 1d)]
    public async Task InvalidRecoveryInterval_ShowsErrorAndDisablesSave(
        double recoveryMinutes,
        double recoverySeconds)
    {
        (GameEditorViewModel viewModel, _, _) =
            await CreateForAddAsync();
        viewModel.Name = "Game";

        viewModel.RecoveryMinutes = recoveryMinutes;
        viewModel.RecoverySeconds = recoverySeconds;

        Assert.IsNotNull(viewModel.RecoveryIntervalError);
        Assert.IsFalse(viewModel.CanSave);
    }

    [TestMethod]
    public async Task SaveAsync_PersistsRecoverySecondsAndNotificationState()
    {
        (GameEditorViewModel viewModel, _, _) =
            await CreateForAddAsync();
        viewModel.Name = "Game";
        viewModel.RecoverySeconds = 30;
        viewModel.IsNotificationEnabled = false;

        GameEntry? saved = await viewModel.SaveAsync(
            CancellationToken.None);

        Assert.IsNotNull(saved);
        Assert.AreEqual(30, saved.RecoverySeconds);
        Assert.IsFalse(saved.IsNotificationEnabled);
    }

    [TestMethod]
    public async Task MetadataOnlyEdit_PreservesBaseAndRecordedTime()
    {
        GameEntry original = CreateEntry();
        (GameEditorViewModel viewModel, GameManager manager, _) =
            await CreateForEditAsync(original);

        viewModel.Name = "Renamed";
        viewModel.ImageAssetId = Guid.NewGuid().ToString("N");
        GameEntry? saved = await viewModel.SaveAsync(
            CancellationToken.None);

        Assert.IsNotNull(saved);
        Assert.AreEqual("Renamed", saved.Name);
        Assert.AreEqual(original.BaseStamina, saved.BaseStamina);
        Assert.AreEqual(original.RecordedAtUtc, saved.RecordedAtUtc);
        Assert.AreEqual(saved, manager.Games.Single());
    }

    [TestMethod]
    public async Task RefreshElapsedWarning_WhenNaturalRecoveryAdvanced_ShowsWarning()
    {
        GameEntry original = CreateEntry() with
        {
            BaseStamina = 40,
            RecordedAtUtc = NowUtc,
        };
        (GameEditorViewModel viewModel, _, FakeClock clock) =
            await CreateForEditAsync(original);

        Assert.IsFalse(viewModel.HasElapsedWarning);
        clock.UtcNow = NowUtc.AddMinutes(10);

        viewModel.RefreshElapsedWarning();

        Assert.IsTrue(viewModel.HasElapsedWarning);
        Assert.Contains("42", viewModel.ElapsedWarning!);
        Assert.AreEqual(40d, viewModel.CurrentStamina);
    }

    [TestMethod]
    public async Task SaveAsync_WhenNaturalRecoveryAdvanced_RequiresConfirmation()
    {
        GameEntry original = CreateEntry() with
        {
            BaseStamina = 40,
            RecordedAtUtc = NowUtc,
        };
        (GameEditorViewModel viewModel, GameManager manager, FakeClock clock) =
            await CreateForEditAsync(original);
        clock.UtcNow = NowUtc.AddMinutes(10);

        GameEntry? firstAttempt = await viewModel.SaveAsync(
            CancellationToken.None);

        Assert.IsNull(firstAttempt);
        Assert.IsTrue(viewModel.HasElapsedWarning);
        Assert.AreEqual(original, manager.Games.Single());

        GameEntry? confirmed = await viewModel.SaveAsync(
            CancellationToken.None);

        Assert.IsNotNull(confirmed);
    }

    [TestMethod]
    public async Task DeleteConfirmation_BackPreservesUnsavedDraft()
    {
        (GameEditorViewModel viewModel, _, _) =
            await CreateForEditAsync(CreateEntry());
        viewModel.Name = "Unsaved name";
        viewModel.CurrentStamina = 77;

        viewModel.RequestDeleteCommand.Execute(null);

        Assert.AreEqual(
            GameEditorState.DeleteConfirmation,
            viewModel.State);
        Assert.IsFalse(viewModel.IsEditing);

        viewModel.BackToEditingCommand.Execute(null);

        Assert.AreEqual(GameEditorState.Editing, viewModel.State);
        Assert.AreEqual("Unsaved name", viewModel.Name);
        Assert.AreEqual(77d, viewModel.CurrentStamina);
    }

    private static async Task<(
        GameEditorViewModel ViewModel,
        GameManager Manager,
        FakeClock Clock)> CreateForAddAsync()
    {
        FakeClock clock = new(NowUtc);
        GameManager manager = new(
            new MemoryDataStore(),
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        await manager.InitializeAsync(
            manager.CurrentData,
            CancellationToken.None);
        return (new GameEditorViewModel(manager, clock), manager, clock);
    }

    private static async Task<(
        GameEditorViewModel ViewModel,
        GameManager Manager,
        FakeClock Clock)> CreateForEditAsync(GameEntry entry)
    {
        FakeClock clock = new(NowUtc);
        GameManager manager = new(
            new MemoryDataStore(),
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        await manager.InitializeAsync(
            manager.CurrentData with
            {
                Games = ImmutableArray.Create(entry),
            },
            CancellationToken.None);
        return (
            new GameEditorViewModel(manager, clock, entry),
            manager,
            clock);
    }

    private static GameEntry CreateEntry() => new(
        Guid.NewGuid(),
        "Original",
        BaseStamina: 40,
        MaxStamina: 100,
        RecoveryMinutes: 5,
        RecordedAtUtc: NowUtc,
        ImageAssetId: null,
        SortOrder: 0,
        RecoverySeconds: 0,
        IsNotificationEnabled: true);

    private sealed class MemoryDataStore : ILocalDataStore
    {
        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            DataEnvelope envelope,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
