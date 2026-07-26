using StaminaManager.Core.Models;

namespace StaminaManager.Core.Calculations;

public readonly record struct WindowSize(int Width, int Height);

public readonly record struct WindowWorkArea(
    int X,
    int Y,
    int Width,
    int Height);

public readonly record struct WindowBoundsSnapshot(
    WindowBounds Bounds,
    double SavedDpi,
    bool IsMaximized = false);

public sealed record WindowBoundsProfile(
    string PersistenceId,
    WindowSize InitialSize,
    WindowSize MinimumSize);

public static class WindowBoundsPolicy
{
    private const double DefaultDpi = 96d;
    private const double MinimumSafeDpi = 48d;
    private const double MaximumSafeDpi = 768d;

    private static readonly WindowBoundsProfile StandardProfile = new(
        "StaminaManager.Standard",
        new WindowSize(1120, 760),
        new WindowSize(520, 520));

    private static readonly WindowBoundsProfile CompactProfile = new(
        "StaminaManager.Compact",
        new WindowSize(420, 520),
        new WindowSize(360, 480));

    public static WindowBoundsProfile GetProfile(
        AppDisplayMode displayMode) => displayMode switch
    {
        AppDisplayMode.Standard => StandardProfile,
        AppDisplayMode.Compact => CompactProfile,
        _ => throw new ArgumentOutOfRangeException(nameof(displayMode)),
    };

    public static WindowBounds Restore(
        AppDisplayMode displayMode,
        WindowBoundsSnapshot? snapshot,
        WindowWorkArea nearestWorkArea,
        double currentDpi)
    {
        WindowBoundsProfile profile = GetProfile(displayMode);
        double safeCurrentDpi = NormalizeDpi(currentDpi);
        int minimumWidth = Scale(profile.MinimumSize.Width, safeCurrentDpi);
        int minimumHeight = Scale(profile.MinimumSize.Height, safeCurrentDpi);
        WindowWorkArea safeWorkArea = NormalizeWorkArea(
            nearestWorkArea,
            minimumWidth,
            minimumHeight);

        WindowBounds sourceBounds = snapshot?.Bounds ?? new WindowBounds(
            safeWorkArea.X,
            safeWorkArea.Y,
            profile.InitialSize.Width,
            profile.InitialSize.Height);
        double savedDpi = NormalizeDpi(snapshot?.SavedDpi ?? DefaultDpi);
        double scale = safeCurrentDpi / savedDpi;
        int requestedWidth = sourceBounds.Width > 0
            ? Scale(sourceBounds.Width, scale, isRatio: true)
            : minimumWidth;
        int requestedHeight = sourceBounds.Height > 0
            ? Scale(sourceBounds.Height, scale, isRatio: true)
            : minimumHeight;

        int width = Math.Min(
            Math.Max(requestedWidth, minimumWidth),
            safeWorkArea.Width);
        int height = Math.Min(
            Math.Max(requestedHeight, minimumHeight),
            safeWorkArea.Height);
        int x = ClampPosition(
            sourceBounds.X,
            safeWorkArea.X,
            safeWorkArea.Width,
            width);
        int y = ClampPosition(
            sourceBounds.Y,
            safeWorkArea.Y,
            safeWorkArea.Height,
            height);

        return new WindowBounds(x, y, width, height);
    }

    private static WindowWorkArea NormalizeWorkArea(
        WindowWorkArea workArea,
        int minimumWidth,
        int minimumHeight) => new(
            workArea.X,
            workArea.Y,
            workArea.Width > 0 ? workArea.Width : minimumWidth,
            workArea.Height > 0 ? workArea.Height : minimumHeight);

    private static double NormalizeDpi(double dpi)
    {
        if (!double.IsFinite(dpi) || dpi <= 0)
        {
            return DefaultDpi;
        }

        return Math.Clamp(dpi, MinimumSafeDpi, MaximumSafeDpi);
    }

    private static int Scale(
        int value,
        double factor,
        bool isRatio = false)
    {
        double result = isRatio
            ? value * factor
            : value * (factor / DefaultDpi);
        if (!double.IsFinite(result) || result >= int.MaxValue)
        {
            return int.MaxValue;
        }

        return Math.Max(1, (int)Math.Round(result));
    }

    private static int ClampPosition(
        int value,
        int workAreaStart,
        int workAreaLength,
        int windowLength)
    {
        long minimum = workAreaStart;
        long maximum = minimum + workAreaLength - windowLength;
        long clamped = Math.Clamp((long)value, minimum, maximum);
        return (int)Math.Clamp(clamped, int.MinValue, int.MaxValue);
    }
}
