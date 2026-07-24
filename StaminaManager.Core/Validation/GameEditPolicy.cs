using StaminaManager.Core.Models;

namespace StaminaManager.Core.Validation;

public static class GameEditPolicy
{
    public static GameEntry Apply(
        GameEntry original,
        GameDraft initialDraft,
        GameDraft editedDraft,
        DateTimeOffset savedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(initialDraft);
        ArgumentNullException.ThrowIfNull(editedDraft);

        bool hasStaminaChanges =
            initialDraft.CurrentStamina != editedDraft.CurrentStamina ||
            initialDraft.MaxStamina != editedDraft.MaxStamina ||
            initialDraft.RecoveryMinutes != editedDraft.RecoveryMinutes;

        return original with
        {
            Name = editedDraft.Name,
            BaseStamina = hasStaminaChanges
                ? editedDraft.CurrentStamina
                : original.BaseStamina,
            MaxStamina = editedDraft.MaxStamina,
            RecoveryMinutes = editedDraft.RecoveryMinutes,
            RecordedAtUtc = hasStaminaChanges
                ? savedAtUtc.ToUniversalTime()
                : original.RecordedAtUtc,
            ImageAssetId = editedDraft.ImageAssetId,
        };
    }
}
