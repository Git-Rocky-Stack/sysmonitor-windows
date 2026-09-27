using Windows.UI.ViewManagement;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// Whether the console may move: Windows' Animation effects setting (Settings, Accessibility, Visual effects),
/// which is what System-X's <c>prefers-reduced-motion</c> reads in a browser (styles.css :2624-2655).
/// <para>
/// With it off nothing loops: a warn lamp holds lit rather than freezing dark, since a blink stopped mid-cycle
/// would hide the warning; a VU meter's tip stops flickering; the busy sweep stays still. Each instrument asks
/// when it enters a state, so a change of the setting reaches it the next time its state changes or it loads.
/// </para>
/// </summary>
internal static class Motion
{
    private static readonly UISettings Settings = new();

    public static bool IsReduced => !Settings.AnimationsEnabled;
}
