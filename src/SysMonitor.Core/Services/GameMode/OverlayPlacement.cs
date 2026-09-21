namespace SysMonitor.Core.Services.GameMode;

/// <summary>A rectangle in screen coordinates. X and Y may be negative on a display left of or above the primary one.</summary>
public readonly record struct OverlayBounds(int X, int Y, int Width, int Height);

/// <summary>
/// Works out where the overlay sits for a chosen corner.
///
/// <para>
/// The arithmetic lives in Core, away from WinUI, because the case that matters cannot be seen by looking at
/// a running app on one monitor: a display to the left of the primary one has a negative X, and a taskbar
/// down the side of the screen moves the work area's origin. A corner worked out from the window size alone
/// lands on the wrong screen on both.
/// </para>
/// </summary>
public static class OverlayPlacement
{
    /// <summary>How far the overlay sits from the edges of the work area, in pixels.</summary>
    public const int Margin = 12;

    /// <summary>
    /// Where a <paramref name="width"/> by <paramref name="height"/> overlay goes for
    /// <paramref name="position"/> within <paramref name="workArea"/>.
    /// </summary>
    public static OverlayBounds Place(OverlayPosition position, OverlayBounds workArea, int width, int height)
    {
        var left = workArea.X + Margin;
        var top = workArea.Y + Margin;
        var right = workArea.X + workArea.Width - width - Margin;
        var bottom = workArea.Y + workArea.Height - height - Margin;

        var (x, y) = position switch
        {
            OverlayPosition.TopLeft => (left, top),
            OverlayPosition.TopRight => (right, top),
            OverlayPosition.BottomLeft => (left, bottom),
            OverlayPosition.BottomRight => (right, bottom),
            _ => (right, top),
        };

        // An overlay wider or taller than the work area would otherwise start off the left or top edge,
        // where its own drag handle cannot be reached.
        return new OverlayBounds(
            Math.Max(workArea.X, x),
            Math.Max(workArea.Y, y),
            width,
            height);
    }
}
