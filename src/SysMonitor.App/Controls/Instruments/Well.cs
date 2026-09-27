using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// Layer 2 of the console's depth: a recessed well carved into a faceplate, the ground every live reading sits
/// on (System-X styles.css :1451-1462).
/// <para>
/// A well is dark in both shifts, as the black screens on silver hardware are. It sets its own theme to Dark,
/// as a local value, so everything inside it - text on the silver ramp, a chip, a cap - resolves the Night Ops
/// colours on Day Shift too; that is what System-X's <c>.well</c> rule does by re-declaring the ramp. A value
/// set in the template instead would not hold: the template's root takes the control's theme. High Contrast
/// still wins, as it does over any requested theme.
/// </para>
/// </summary>
public sealed class Well : ContentControl
{
    public Well()
    {
        RequestedTheme = ElementTheme.Dark;
    }
}
