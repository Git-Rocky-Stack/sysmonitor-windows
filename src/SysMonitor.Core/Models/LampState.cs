namespace SysMonitor.Core.Models;

/// <summary>
/// The Command Console's status vocabulary: what a lamp, a module's rail or a chip reports (System-X
/// DESIGN.md section 7). Every state is shown as a word first and a colour second, so it reads to someone who
/// cannot tell the LED hues apart.
/// <para>
/// Red comes in two forms that mean opposite things. <see cref="Armed"/> is steady red: live, running, acting
/// on the machine, and not an error. <see cref="Warn"/> blinks: a question nobody has answered yet. A failure
/// that is no longer waiting for anyone is <see cref="NoGo"/>, steady, never <see cref="Warn"/>.
/// </para>
/// </summary>
public enum LampState
{
    /// <summary>Unlit: nothing to report, or no reading to report on.</summary>
    Off,

    /// <summary>Running, healthy, clean.</summary>
    Go,

    /// <summary>Caution, pending, degraded.</summary>
    Hold,

    /// <summary>An unacknowledged warning. Blinks at 1 Hz until it is dealt with.</summary>
    Warn,

    /// <summary>A settled fault: failed, and known to have failed. Steady.</summary>
    NoGo,

    /// <summary>Informational, or in progress.</summary>
    Exec,

    /// <summary>Live: the app is operating on the system, or the user has staged something that will.</summary>
    Armed,
}

/// <summary>
/// Turns a reading into a lamp state, so every page that grades a percentage grades it the same way. The
/// defaults are System-X's (<c>usageState</c> and <c>scoreState</c> in src/components/console/Lamp.tsx); a
/// page with thresholds of its own passes them, so moving a page onto lamps never changes where its colours
/// change.
/// </summary>
public static class LampStates
{
    /// <summary>
    /// A reading where higher is worse - memory pressure, disk use, temperature as a share of its limit.
    /// A reading that is not a number is <see cref="LampState.Off"/>: a sensor that reported nothing is not
    /// healthy, and a green lamp over a missing reading would say it was.
    /// </summary>
    public static LampState Usage(double percent, double critical = 90, double warning = 75)
    {
        if (!double.IsFinite(percent)) return LampState.Off;
        if (percent >= critical) return LampState.NoGo;
        if (percent >= warning) return LampState.Hold;
        return LampState.Go;
    }

    /// <summary>
    /// A reading where higher is better - a health score. As with <see cref="Usage"/>, a reading that is not
    /// a number is <see cref="LampState.Off"/>, never a grade.
    /// </summary>
    public static LampState Score(double score, double good = 80, double moderate = 60)
    {
        if (!double.IsFinite(score)) return LampState.Off;
        if (score >= good) return LampState.Go;
        if (score >= moderate) return LampState.Hold;
        return LampState.NoGo;
    }
}
