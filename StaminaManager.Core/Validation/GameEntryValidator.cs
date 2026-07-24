using System.Collections.Immutable;
using StaminaManager.Core.Calculations;
using StaminaManager.Core.Models;

namespace StaminaManager.Core.Validation;

public static class GameEntryValidator
{
    public const int MaxStaminaValue = 1_000_000;
    public const int MaxRecoveryMinutes = 525_600;
    public const int MaxGameCount = 100;

    private const string NameRequiredMessage =
        "ゲーム名を入力してください。";
    private const string CurrentStaminaRangeMessage =
        "現在のスタミナは0～1,000,000の整数で入力してください。";
    private const string MaxStaminaRangeMessage =
        "最大スタミナは1～1,000,000の整数で入力してください。";
    private const string RecoveryMinutesRangeMessage =
        "回復時間は1～525,600分の整数で入力してください。";
    private const string FullTimeOutOfRangeMessage =
        "この最大値と回復時間では満タン時刻を計算できません。" +
        "値を小さくしてください。";

    public static ValidationResult Validate(
        GameDraft draft,
        DateTimeOffset proposedRecordedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(draft);

        ImmutableDictionary<string, ImmutableArray<string>>.Builder errors =
            ImmutableDictionary.CreateBuilder<
                string,
                ImmutableArray<string>>(StringComparer.Ordinal);

        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            AddError(nameof(GameDraft.Name), NameRequiredMessage);
        }

        if (draft.CurrentStamina is < 0 or > MaxStaminaValue)
        {
            AddError(
                nameof(GameDraft.CurrentStamina),
                CurrentStaminaRangeMessage);
        }

        if (draft.MaxStamina is < 1 or > MaxStaminaValue)
        {
            AddError(
                nameof(GameDraft.MaxStamina),
                MaxStaminaRangeMessage);
        }

        if (draft.RecoveryMinutes is < 1 or > MaxRecoveryMinutes)
        {
            AddError(
                nameof(GameDraft.RecoveryMinutes),
                RecoveryMinutesRangeMessage);
        }

        if (errors.Count == 0 &&
            draft.CurrentStamina < draft.MaxStamina)
        {
            ValidateFullTime(draft, proposedRecordedAtUtc, AddError);
        }

        return new ValidationResult(errors.ToImmutable());

        void AddError(string fieldKey, string message)
        {
            errors[fieldKey] = ImmutableArray.Create(message);
        }
    }

    private static void ValidateFullTime(
        GameDraft draft,
        DateTimeOffset proposedRecordedAtUtc,
        Action<string, string> addError)
    {
        GameEntry proposedEntry = new(
            Id: Guid.Empty,
            Name: draft.Name,
            BaseStamina: draft.CurrentStamina,
            MaxStamina: draft.MaxStamina,
            RecoveryMinutes: draft.RecoveryMinutes,
            RecordedAtUtc: proposedRecordedAtUtc,
            ImageAssetId: draft.ImageAssetId,
            SortOrder: 0);

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
                FullTimeOutOfRangeMessage);
        }
    }
}
