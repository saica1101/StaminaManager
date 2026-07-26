using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaminaManager.Application;
using StaminaManager.Core.Abstractions;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;
using StaminaManager.Core.Validation;

namespace StaminaManager.ViewModels;

public enum GameEditorState
{
    Editing,
    DeleteConfirmation,
}

public sealed partial class GameEditorViewModel : ObservableObject
{
    private const string IntegerInputMessage =
        "整数で入力してください。";
    private readonly GameManager _gameManager;
    private readonly IClock _clock;
    private readonly GameEntry? _originalEntry;
    private readonly GameDraft _initialDraft;
    private bool _isElapsedWarningAcknowledged;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial double CurrentStamina { get; set; }

    [ObservableProperty]
    public partial double MaxStamina { get; set; }

    [ObservableProperty]
    public partial double RecoveryMinutes { get; set; }

    [ObservableProperty]
    public partial string? ImageAssetId { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditing))]
    [NotifyPropertyChangedFor(nameof(CanPrimaryAction))]
    [NotifyPropertyChangedFor(nameof(DialogCloseButtonText))]
    [NotifyPropertyChangedFor(nameof(DialogPrimaryActionText))]
    public partial GameEditorState State { get; private set; } =
        GameEditorState.Editing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasElapsedWarning))]
    public partial string? ElapsedWarning { get; private set; }

    [ObservableProperty]
    public partial string? NameError { get; private set; }

    [ObservableProperty]
    public partial string? CurrentStaminaError { get; private set; }

    [ObservableProperty]
    public partial string? MaxStaminaError { get; private set; }

    [ObservableProperty]
    public partial string? RecoveryMinutesError { get; private set; }

    [ObservableProperty]
    public partial string? GeneralError { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanPrimaryAction))]
    public partial bool IsBusy { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyPropertyChangedFor(nameof(CanPrimaryAction))]
    public partial bool IsValid { get; private set; }

    public GameEditorViewModel(
        GameManager gameManager,
        IClock clock,
        GameEntry? entry = null)
    {
        ArgumentNullException.ThrowIfNull(gameManager);
        ArgumentNullException.ThrowIfNull(clock);
        _gameManager = gameManager;
        _clock = clock;
        _originalEntry = entry;

        if (entry is null)
        {
            Name = string.Empty;
            CurrentStamina = 0;
            MaxStamina = 200;
            RecoveryMinutes = 8;
            ImageAssetId = null;
        }
        else
        {
            StaminaSnapshot snapshot = StaminaCalculator.Calculate(
                entry,
                _clock.UtcNow);
            Name = entry.Name;
            CurrentStamina = snapshot.Current;
            MaxStamina = entry.MaxStamina;
            RecoveryMinutes = entry.RecoveryMinutes;
            ImageAssetId = entry.ImageAssetId;
        }

        _initialDraft = CreateDraftOrThrow();
        Validate();
    }

    public bool IsNew => _originalEntry is null;

    public bool CanDelete => _originalEntry is not null;

    public bool IsEditing => State == GameEditorState.Editing;

    public bool CanSave => IsEditing && IsValid && !IsBusy;

    public bool CanPrimaryAction => State == GameEditorState.DeleteConfirmation
        ? !IsBusy
        : CanSave;

    public bool HasElapsedWarning =>
        !string.IsNullOrWhiteSpace(ElapsedWarning);

    public string DialogTitle => IsNew
        ? "ゲームを追加"
        : "ゲームを編集";

    public string ActionText => IsNew ? "追加" : "保存";

    public string DialogPrimaryActionText =>
        State == GameEditorState.DeleteConfirmation
            ? "削除"
            : ActionText;

    public string DialogCloseButtonText =>
        State == GameEditorState.DeleteConfirmation
            ? "戻る"
            : "キャンセル";

    public GameEntry? OriginalEntry => _originalEntry;

    public void ShowGeneralError(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        GeneralError = message;
    }

    public void RefreshElapsedWarning()
    {
        if (_originalEntry is null)
        {
            ElapsedWarning = null;
            return;
        }

        int calculatedCurrent = StaminaCalculator.Calculate(
            _originalEntry,
            _clock.UtcNow).Current;
        ElapsedWarning = calculatedCurrent == _initialDraft.CurrentStamina
            ? null
            : $"この画面を開いている間に自然回復が進みました。現在の計算値は {calculatedCurrent} です。入力値を確認してから保存してください。";
    }

    public async Task<GameEntry?> SaveAsync(
        CancellationToken cancellationToken)
    {
        EnsureEditing();
        Validate();
        if (!CanSave)
        {
            throw new InvalidOperationException(
                "入力内容を確認してください。");
        }

        RefreshElapsedWarning();
        if (HasElapsedWarning && !_isElapsedWarningAcknowledged)
        {
            _isElapsedWarningAcknowledged = true;
            return null;
        }

        GameDraft editedDraft = CreateDraftOrThrow();
        IsBusy = true;
        GeneralError = null;
        try
        {
            return _originalEntry is null
                ? await _gameManager.AddAsync(
                        editedDraft,
                        cancellationToken)
                : await _gameManager.EditAsync(
                        _originalEntry.Id,
                        _initialDraft,
                        editedDraft,
                        cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or InvalidOperationException)
        {
            GeneralError = exception is IOException
                    or UnauthorizedAccessException
                ? "データを保存できませんでした。もう一度お試しください。"
                : "入力内容を保存できません。各項目を確認してください。";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> DeleteAsync(
        CancellationToken cancellationToken)
    {
        if (_originalEntry is null
            || State != GameEditorState.DeleteConfirmation)
        {
            throw new InvalidOperationException(
                "削除確認中のゲームだけを削除できます。");
        }

        IsBusy = true;
        GeneralError = null;
        try
        {
            return await _gameManager.DeleteAsync(
                    _originalEntry.Id,
                    cancellationToken);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            GeneralError =
                "ゲームを削除できませんでした。もう一度お試しください。";
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRequestDelete))]
    private void RequestDelete()
    {
        State = GameEditorState.DeleteConfirmation;
        GeneralError = null;
    }

    [RelayCommand(CanExecute = nameof(CanGoBackToEditing))]
    private void BackToEditing()
    {
        State = GameEditorState.Editing;
        GeneralError = null;
    }

    private bool CanRequestDelete() =>
        CanDelete && IsEditing && !IsBusy;

    private bool CanGoBackToEditing() =>
        State == GameEditorState.DeleteConfirmation && !IsBusy;

    partial void OnNameChanged(string value) => ValidateIfReady();

    partial void OnCurrentStaminaChanged(double value) => ValidateIfReady();

    partial void OnMaxStaminaChanged(double value) => ValidateIfReady();

    partial void OnRecoveryMinutesChanged(double value) => ValidateIfReady();

    partial void OnStateChanged(GameEditorState value)
    {
        OnPropertyChanged(nameof(CanSave));
        RequestDeleteCommand.NotifyCanExecuteChanged();
        BackToEditingCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        RequestDeleteCommand.NotifyCanExecuteChanged();
        BackToEditingCommand.NotifyCanExecuteChanged();
    }

    private void ValidateIfReady()
    {
        if (_initialDraft is not null)
        {
            Validate();
        }
    }

    private void Validate()
    {
        NameError = null;
        CurrentStaminaError = GetIntegerError(CurrentStamina);
        MaxStaminaError = GetIntegerError(MaxStamina);
        RecoveryMinutesError = GetIntegerError(RecoveryMinutes);

        GameDraft draft = new(
            Name ?? string.Empty,
            ToValidationInt(CurrentStamina),
            ToValidationInt(MaxStamina),
            ToValidationInt(RecoveryMinutes),
            ImageAssetId);
        ValidationResult result = GameEntryValidator.Validate(
            draft,
            _clock.UtcNow);
        NameError ??= FirstError(result, nameof(GameDraft.Name));
        CurrentStaminaError ??= FirstError(
            result,
            nameof(GameDraft.CurrentStamina));
        MaxStaminaError ??= FirstError(
            result,
            nameof(GameDraft.MaxStamina));
        RecoveryMinutesError ??= FirstError(
            result,
            nameof(GameDraft.RecoveryMinutes));
        IsValid = NameError is null
            && CurrentStaminaError is null
            && MaxStaminaError is null
            && RecoveryMinutesError is null;
        OnPropertyChanged(nameof(CanSave));
    }

    private GameDraft CreateDraftOrThrow() => new(
        Name,
        ToIntOrThrow(CurrentStamina, nameof(CurrentStamina)),
        ToIntOrThrow(MaxStamina, nameof(MaxStamina)),
        ToIntOrThrow(RecoveryMinutes, nameof(RecoveryMinutes)),
        ImageAssetId);

    private static string? GetIntegerError(double value) =>
        double.IsFinite(value)
            && value == Math.Truncate(value)
            && value is >= int.MinValue and <= int.MaxValue
                ? null
                : IntegerInputMessage;

    private static int ToValidationInt(double value) =>
        GetIntegerError(value) is null
            ? checked((int)value)
            : int.MinValue;

    private static int ToIntOrThrow(double value, string fieldName)
    {
        if (GetIntegerError(value) is not null)
        {
            throw new InvalidOperationException(
                $"{fieldName} は整数で入力してください。");
        }

        return checked((int)value);
    }

    private static string? FirstError(
        ValidationResult result,
        string fieldName) =>
        result.Errors.TryGetValue(fieldName, out var errors)
            ? errors.FirstOrDefault()
            : null;

    private void EnsureEditing()
    {
        if (!IsEditing)
        {
            throw new InvalidOperationException(
                "削除確認中は保存できません。");
        }
    }
}
