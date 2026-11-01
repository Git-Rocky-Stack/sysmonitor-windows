namespace SysMonitor.Core.Models;

/// <summary>What a process meter's ACT light is doing.</summary>
public enum ActivityPhase
{
    /// <summary>No work shown: the lens is dark.</summary>
    Dark,

    /// <summary>Work under way: the light flickers like a drive's ACT light.</summary>
    Busy,

    /// <summary>The meter is full: the light holds lit.</summary>
    Done,
}

/// <summary>One key of the light's timeline: from <see cref="At"/>, it is lit or not.</summary>
public readonly record struct ActivityBlink(TimeSpan At, bool Lit);

/// <summary>
/// The ACT light on a process's progress meter, read off the meter's own numbers: dark until the meter is shown,
/// a strike of three quick flashes the moment the work begins, then an irregular flicker - lit more often than
/// not, with drop-outs of uneven length, as a drive's activity light is - and held lit once the meter is full.
/// <para>
/// The flicker is deliberately not a metronome. An evenly spaced blink is the console's warn signal (a warn lamp
/// blinks at 1 Hz), and work under way must never read as a warning.
/// </para>
/// </summary>
public static class ActivityLight
{
    private static readonly IReadOnlyList<ActivityBlink> StrikeKeys =
    [
        new(TimeSpan.Zero, true),
        new(TimeSpan.FromMilliseconds(60), false),
        new(TimeSpan.FromMilliseconds(120), true),
        new(TimeSpan.FromMilliseconds(180), false),
        new(TimeSpan.FromMilliseconds(240), true),
    ];

    private static readonly IReadOnlyList<ActivityBlink> CadenceKeys =
    [
        new(TimeSpan.Zero, true),
        new(TimeSpan.FromMilliseconds(130), false),
        new(TimeSpan.FromMilliseconds(190), true),
        new(TimeSpan.FromMilliseconds(420), false),
        new(TimeSpan.FromMilliseconds(470), true),
        new(TimeSpan.FromMilliseconds(560), false),
        new(TimeSpan.FromMilliseconds(680), true),
    ];

    /// <summary>Three quick flashes that end lit, when work begins.</summary>
    public static IReadOnlyList<ActivityBlink> Strike => StrikeKeys;

    /// <summary>How long the strike runs before the flicker takes over.</summary>
    public static TimeSpan StrikeDuration { get; } = TimeSpan.FromMilliseconds(320);

    /// <summary>One cycle of the flicker while work is under way; it repeats.</summary>
    public static IReadOnlyList<ActivityBlink> Cadence => CadenceKeys;

    /// <summary>The length of one cycle of <see cref="Cadence"/>.</summary>
    public static TimeSpan CadenceDuration { get; } = TimeSpan.FromMilliseconds(910);

    /// <summary>
    /// The light's phase for a meter: dark while it is not shown, busy while its work runs - from nought, since a
    /// process meter is shown the moment the work begins - and done once it is full. Work with no extent is busy
    /// whatever its value; a reading that is not a number, or a range with no width, lights nothing.
    /// </summary>
    public static ActivityPhase PhaseOf(bool isShowing, bool isIndeterminate, double value, double minimum, double maximum)
    {
        if (!isShowing)
            return ActivityPhase.Dark;

        if (isIndeterminate)
            return ActivityPhase.Busy;

        if (!double.IsFinite(value) || !(maximum > minimum))
            return ActivityPhase.Dark;

        return value >= maximum ? ActivityPhase.Done : ActivityPhase.Busy;
    }

    /// <summary>
    /// Whether the light strikes: work beginning, which is the light coming up busy from dark or from a finished
    /// run, or a busy meter falling back, which is a new run starting.
    /// </summary>
    public static bool Strikes(ActivityPhase from, ActivityPhase to, double previousValue, double value)
    {
        if (to != ActivityPhase.Busy)
            return false;

        return from != ActivityPhase.Busy || value < previousValue;
    }
}
