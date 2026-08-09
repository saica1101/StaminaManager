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
    public async Task DisplayText_ResolvesFromInjectedResourcesInJapaneseAndEnglish()
    {
        foreach (string language in new[] { "ja-JP", "en-US" })
        {
            FakeAppResourceService resources = CreateResources(language);
            (GameEditorViewModel addViewModel, _, _) =
                await CreateForAddAsync(resources);
            (GameEditorViewModel editViewModel, _, _) =
                await CreateForEditAsync(CreateEntry(), resources);

            Assert.AreEqual(
                $"{language}.add-title",
                addViewModel.DialogTitle);
            Assert.AreEqual(
                $"{language}.add-action",
                addViewModel.ActionText);
            Assert.AreEqual(
                $"{language}.edit-title",
                editViewModel.DialogTitle);
            Assert.AreEqual(
                $"{language}.save-action",
                editViewModel.ActionText);
            Assert.AreEqual(
                $"{language}.cancel-action",
                addViewModel.DialogCloseButtonText);

            editViewModel.RequestDeleteCommand.Execute(null);

            Assert.AreEqual(
                $"{language}.delete-action",
                editViewModel.DialogPrimaryActionText);
            Assert.AreEqual(
                $"{language}.back-action",
                editViewModel.DialogCloseButtonText);
        }
    }

    [TestMethod]
    public async Task ValidationAndIntegerErrors_ResolveFromInjectedResources()
    {
        foreach (string language in new[] { "ja-JP", "en-US" })
        {
            FakeAppResourceService resources = CreateResources(language);
            (GameEditorViewModel viewModel, _, _) =
                await CreateForAddAsync(resources);

            viewModel.Name = " ";
            Assert.AreEqual(
                $"{language}.name-required",
                viewModel.NameError);

            viewModel.Name = "Game";
            viewModel.CurrentStamina = -1;
            Assert.AreEqual(
                $"{language}.current-stamina-range",
                viewModel.CurrentStaminaError);

            viewModel.CurrentStamina = 40;
            viewModel.MaxStamina = 0;
            Assert.AreEqual(
                $"{language}.max-stamina-range",
                viewModel.MaxStaminaError);

            viewModel.MaxStamina = 100;
            viewModel.RecoveryMinutes = 1.5;
            Assert.AreEqual(
                $"{language}.integer",
                viewModel.RecoveryMinutesError);
        }
    }

    [TestMethod]
    public async Task SaveAndDeleteErrors_ResolveFromInjectedResources()
    {
        foreach (string language in new[] { "ja-JP", "en-US" })
        {
            FakeAppResourceService resources = CreateResources(language);
            MemoryDataStore saveStore = new()
            {
                SaveException = new IOException(),
            };
            (GameEditorViewModel saveViewModel, _, _) =
                await CreateForAddAsync(resources, saveStore);
            saveViewModel.Name = "Game";

            await Assert.ThrowsExactlyAsync<IOException>(
                () => saveViewModel.SaveAsync(CancellationToken.None));

            Assert.AreEqual(
                $"{language}.save-error",
                saveViewModel.GeneralError);

            MemoryDataStore deleteStore = new()
            {
                SaveException = new IOException(),
            };
            (GameEditorViewModel deleteViewModel, _, _) =
                await CreateForEditAsync(
                    CreateEntry(),
                    resources,
                    deleteStore);
            deleteViewModel.RequestDeleteCommand.Execute(null);

            await Assert.ThrowsExactlyAsync<IOException>(
                () => deleteViewModel.DeleteAsync(CancellationToken.None));

            Assert.AreEqual(
                $"{language}.delete-error",
                deleteViewModel.GeneralError);
        }
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
    public async Task ElapsedWarning_ResolvesFromInjectedResourcesInJapaneseAndEnglish()
    {
        GameEntry original = CreateEntry() with
        {
            BaseStamina = 40,
            RecordedAtUtc = NowUtc,
        };

        foreach (string language in new[] { "ja-JP", "en-US" })
        {
            FakeAppResourceService resources = CreateResources(language);
            (GameEditorViewModel viewModel, _, FakeClock clock) =
                await CreateForEditAsync(original, resources);
            clock.UtcNow = NowUtc.AddMinutes(10);

            viewModel.RefreshElapsedWarning();

            Assert.AreEqual(
                $"{language}.elapsed-42",
                viewModel.ElapsedWarning);
        }
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
        FakeClock Clock)> CreateForAddAsync(
        IAppResourceService? resources = null,
        MemoryDataStore? dataStore = null)
    {
        FakeClock clock = new(NowUtc);
        GameManager manager = new(
            dataStore ?? new MemoryDataStore(),
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        await manager.InitializeAsync(
            manager.CurrentData,
            CancellationToken.None);
        return (
            CreateViewModel(
                manager,
                clock,
                resources ?? CreateDefaultJapaneseResources()),
            manager,
            clock);
    }

    private static async Task<(
        GameEditorViewModel ViewModel,
        GameManager Manager,
        FakeClock Clock)> CreateForEditAsync(
        GameEntry entry,
        IAppResourceService? resources = null,
        MemoryDataStore? dataStore = null)
    {
        FakeClock clock = new(NowUtc);
        GameManager manager = new(
            dataStore ?? new MemoryDataStore(),
            clock,
            AppSettings.CreateDefault(AppTheme.Light));
        await manager.InitializeAsync(
            manager.CurrentData with
            {
                Games = ImmutableArray.Create(entry),
            },
            CancellationToken.None);
        return (
            CreateViewModel(
                manager,
                clock,
                resources ?? CreateDefaultJapaneseResources(),
                entry),
            manager,
            clock);
    }

    private static GameEditorViewModel CreateViewModel(
        GameManager manager,
        IClock clock,
        IAppResourceService resources,
        GameEntry? entry = null) =>
        new(manager, clock, resources, entry);

    private static FakeAppResourceService CreateResources(string language) =>
        new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GameEditorAddTitle"] = $"{language}.add-title",
            ["GameEditorEditTitle"] = $"{language}.edit-title",
            ["GameEditorAddAction"] = $"{language}.add-action",
            ["GameEditorSaveAction"] = $"{language}.save-action",
            ["DeleteConfirmButton.Content"] = $"{language}.delete-action",
            ["DeleteBackButton.Content"] = $"{language}.back-action",
            ["GameEditorCancelAction"] = $"{language}.cancel-action",
            ["GameEditorIntegerInputError"] = $"{language}.integer",
            ["GameEditorNameRequiredError"] = $"{language}.name-required",
            ["GameEditorCurrentStaminaOutOfRangeError"] =
                $"{language}.current-stamina-range",
            ["GameEditorMaxStaminaOutOfRangeError"] =
                $"{language}.max-stamina-range",
            ["GameEditorElapsedWarningFormat"] =
                $"{language}.elapsed-{{0}}",
            ["GameEditorSaveError"] = $"{language}.save-error",
            ["GameEditorSaveValidationError"] =
                $"{language}.save-validation-error",
            ["GameEditorDeleteError"] = $"{language}.delete-error",
        });

    private static FakeAppResourceService CreateDefaultJapaneseResources() =>
        new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GameEditorAddAction"] = "追加",
            ["GameEditorSaveAction"] = "保存",
            ["GameEditorElapsedWarningFormat"] =
                "この画面を開いている間に自然回復が進みました。"
                + "現在の計算値は {0} です。入力値を確認してから保存してください。",
        });

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

    private sealed class FakeAppResourceService(
        IReadOnlyDictionary<string, string> values) : IAppResourceService
    {
        public string GetString(string resourceId) =>
            values.TryGetValue(resourceId, out string? value)
                ? value
                : resourceId;

        public string Format(string resourceId, params object?[] args) =>
            string.Format(GetString(resourceId), args);
    }

    private sealed class MemoryDataStore : ILocalDataStore
    {
        public Exception? SaveException { get; init; }

        public Task<DataLoadResult> LoadAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task SaveAsync(
            DataEnvelope envelope,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SaveException is null
                ? Task.CompletedTask
                : Task.FromException(SaveException);
        }

        public Task<RecoveryPromotionResult> PromoteRecoveryAsync(
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
