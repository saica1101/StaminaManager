using StaminaManager.Core.Models;
using System.Diagnostics;

namespace StaminaManager.Core.Validation;

public static class BackdropPolicy
{
    public static BackdropKind NormalizeLegacy(BackdropKind backdrop) =>
        backdrop switch
        {
            BackdropKind.Mica => BackdropKind.Mica,
            BackdropKind.Acrylic => BackdropKind.Acrylic,
            BackdropKind.Blur or BackdropKind.Transparent =>
                BackdropKind.Acrylic,
            BackdropKind.Solid => BackdropKind.Solid,
            _ => throw new ArgumentOutOfRangeException(
                nameof(backdrop),
                backdrop,
                "The backdrop value is not defined."),
        };

    public static bool TryFromSelectionIndex(
        int index,
        out BackdropKind backdrop)
    {
        backdrop = index switch
        {
            0 => BackdropKind.Mica,
            1 => BackdropKind.Acrylic,
            2 => BackdropKind.Solid,
            _ => default,
        };
        return index is >= 0 and <= 2;
    }

    public static int ToSelectionIndex(BackdropKind backdrop) =>
        NormalizeLegacy(backdrop) switch
        {
            BackdropKind.Mica => 0,
            BackdropKind.Acrylic => 1,
            BackdropKind.Solid => 2,
            _ => throw new UnreachableException(),
        };
}
