using StaminaManager.Core.Models;

namespace StaminaManager.Core.Calculations;

public static class WindowClosePolicy
{
    public static bool ShouldMinimizeToTray(
        CloseBehavior closeBehavior,
        bool isExplicitExit) =>
        closeBehavior == CloseBehavior.MinimizeToTray
        && !isExplicitExit;
}
