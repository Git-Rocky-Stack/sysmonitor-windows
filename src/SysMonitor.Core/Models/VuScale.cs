namespace SysMonitor.Core.Models;

/// <summary>The zone a VU meter's segment lights in, by its place on the scale.</summary>
public enum VuZone
{
    Go,
    Hold,
    Orange,
    Red,
}

/// <summary>
/// A VU meter's arithmetic (System-X src/components/console/Vu.tsx): how many segments a reading lights, and
/// which colour each lights in. The warm zones are packed into the top of the scale - a segment past 60% of it
/// is amber, past 80% orange, past 90% red - because the top is where the operator needs to look.
/// </summary>
public static class VuScale
{
    /// <summary>
    /// The segments a reading lights: its share of the range, clamped to it and rounded half up as System-X's
    /// <c>Math.round</c> does. A reading that is not a number, or a range with no width, lights none.
    /// </summary>
    public static int Lit(double value, double minimum, double maximum, int segments)
    {
        var span = maximum - minimum;
        if (segments < 1 || !double.IsFinite(value) || !(span > 0))
            return 0;

        var share = Math.Clamp((value - minimum) / span, 0, 1);
        return (int)Math.Round(share * segments, MidpointRounding.AwayFromZero);
    }

    /// <summary>The zone of the segment at <paramref name="index"/>, counted from the empty end.</summary>
    public static VuZone ZoneOf(int index, int segments)
    {
        var position = (index + 1) / (double)Math.Max(1, segments);
        if (position > 0.9) return VuZone.Red;
        if (position > 0.8) return VuZone.Orange;
        if (position > 0.6) return VuZone.Hold;
        return VuZone.Go;
    }
}
