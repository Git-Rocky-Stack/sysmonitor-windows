using Microsoft.UI.Xaml.Controls;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// A hex socket-cap bolt, 16 across, machined in three layers: the head lit from the top left, a faint ring,
/// and the socket lit from the opposite side (System-X styles.css :1557-1622). A faceplate carries four.
/// <para>
/// Structure, not content: it takes no focus and has no automation peer, so a screen reader passes over it,
/// as System-X's are aria-hidden. It is gone in High Contrast, where its brushes are transparent.
/// </para>
/// </summary>
public sealed class HexBolt : Control
{
    public HexBolt()
    {
        IsTabStop = false;
    }
}
