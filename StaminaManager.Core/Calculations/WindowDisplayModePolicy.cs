using StaminaManager.Core.Models;

namespace StaminaManager.Core.Calculations;

public enum WindowPresenterState
{
    Restored,
    Maximized,
    Minimized,
}

public readonly record struct WindowBounds(
    int X,
    int Y,
    int Width,
    int Height);

public readonly record struct WindowDisplayModeSnapshot(
    WindowBounds RestoredBounds,
    bool ShouldMaximizeOnReturn);

public static class WindowDisplayModePolicy
{
    private const double DefaultDpi = 96d;

    public static int ScaleEffectiveToPhysical(
        int effectivePixels,
        uint dpi)
    {
        double scale = dpi == 0 ? 1d : dpi / DefaultDpi;
        return checked((int)Math.Round(effectivePixels * scale));
    }

    public static WindowDisplayModeSnapshot CaptureSnapshot(
        WindowPresenterState presenterState,
        WindowBounds currentBounds,
        WindowBounds previousRestoredBounds) => presenterState switch
    {
        WindowPresenterState.Restored => new(currentBounds, false),
        WindowPresenterState.Maximized => new(previousRestoredBounds, true),
        _ => new(previousRestoredBounds, false),
    };

    public static bool ShouldCaptureRestoredBounds(
        AppDisplayMode displayMode,
        bool isTransitioning,
        WindowPresenterState presenterState) =>
        displayMode == AppDisplayMode.Standard
        && !isTransitioning
        && presenterState == WindowPresenterState.Restored;
}
