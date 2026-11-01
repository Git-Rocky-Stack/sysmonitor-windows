using Microsoft.UI.Xaml.Controls;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// A stainless socket-cap screw: a round, polished head with a chamfered rim, driven through a hexagonal socket,
/// seated in a counterbore cut into the panel and lit from the top left. It is drawn once on a 16 grid and scaled,
/// so it reads the same at any size: a faceplate carries four at 16 across, a plate and a banner four smaller ones,
/// because every panel on the console is screwed down at its corners.
/// <para>
/// Structure, not content: it takes no focus and has no automation peer, so a screen reader passes over it,
/// as System-X's bolts are aria-hidden. It is gone in High Contrast, where its brushes are transparent.
/// </para>
/// </summary>
public sealed class HexBolt : Control
{
    public HexBolt()
    {
        IsTabStop = false;
    }
}
