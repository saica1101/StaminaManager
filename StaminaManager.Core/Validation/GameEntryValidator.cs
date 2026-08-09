using System.Collections.Immutable;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;

namespace StaminaManager.Core.Validation;

public static class GameEntryValidator
{
    public const string RecoveryIntervalField = "RecoveryInterval";
    public const int MaxStaminaValue = 1_000_000;
    public const int MaxRecoveryMinutes = 525_600;
    public const int MaxGameCount = 100;

    public static ValidationResult Validate(
        GameDraft draft,
        DateTimeOffset proposedRecordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(draft);

        long recoveryIntervalSeconds = checked(
            (long)draft.RecoveryMinutes * 60 + draft.RecoverySeconds);

        ImmutableDictionary<
            string,
            ImmutableArray<ValidationErrorCode>>.Builder errors =
            ImmutableDictionary.CreateBuilder<
                string,
                ImmutableArray<ValidationErrorCode>>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            AddError(
                nameof(GameDraft.Name),
                ValidationErrorCode.NameRequired);
        }

        if (draft.CurrentStamina is < 0 or > MaxStaminaValue)
        {
            AddError(
                nameof(GameDraft.CurrentStamina),
                ValidationErrorCode.CurrentStaminaOutOfRange);
        }

        if (draft.MaxStamina is < 1 or > MaxStaminaValue)
        {
            AddError(
                nameof(GameDraft.MaxStamina),
                ValidationErrorCode.MaxStaminaOutOfRange);
        }

        if (draft.RecoveryMinutes is < 0 or > MaxRecoveryMinutes)
        {
            AddError(
                nameof(GameDraft.RecoveryMinutes),
                ValidationErrorCode.RecoveryMinutesOutOfRange);
        }

        if (draft.RecoverySeconds is < 0 or > 59)
        {
            AddError(
                nameof(GameDraft.RecoverySeconds),
                ValidationErrorCode.RecoverySecondsOutOfRange);
        }

        if (recoveryIntervalSeconds is < 1 or > MaxRecoveryMinutes * 60L)
        {
            AddError(
                RecoveryIntervalField,
                ValidationErrorCode.RecoveryIntervalOutOfRange);
        }

        if (errors.Count == 0 &&
            draft.CurrentStamina < draft.MaxStamina)
        {
            ValidateFullTime(draft, proposedRecordedAtUtc, AddError);
        }

        return new ValidationResult(errors.ToImmutable());

        void AddError(string fieldKey, ValidationErrorCode code)
        {
            errors[fieldKey] = ImmutableArray.Create(code);
        }
    }

    private static void ValidateFullTime(
        GameDraft draft,
        DateTimeOffset proposedRecordedAtUtc,
        Action<string, ValidationErrorCode> addError)
    {
        GameEntry proposedEntry = new(
            Id: Guid.Empty,
            Name: draft.Name,
            BaseStamina: draft.CurrentStamina,
            MaxStamina: draft.MaxStamina,
            RecoveryMinutes: draft.RecoveryMinutes,
            RecordedAtUtc: proposedRecordedAtUtc,
            ImageAssetId: draft.ImageAssetId,
            SortOrder: 0,
            RecoverySeconds: draft.RecoverySeconds,
            IsNotificationEnabled: draft.IsNotificationEnabled);

        try
        {
            _ = StaminaCalculator.Calculate(
                proposedEntry,
                proposedRecordedAtUtc);
        }
        catch (ArgumentOutOfRangeException)
        {
            addError(
                nameof(GameDraft.MaxStamina),
                ValidationErrorCode.FullTimeOutOfRange);
        }
    }
}
