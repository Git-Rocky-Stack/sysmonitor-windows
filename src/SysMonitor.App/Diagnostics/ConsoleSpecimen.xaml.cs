using Microsoft.UI.Xaml.Controls;

namespace SysMonitor.App.Diagnostics;

/// <summary>Every console instrument in every state, for the UI smoke run to show in both shifts.</summary>
public sealed partial class ConsoleSpecimen : UserControl
{
    public ConsoleSpecimen()
    {
        InitializeComponent();
    }

    /// <summary>A line of Silver text inside a well: Night Ops silver in both shifts, or the well leaks.</summary>
    internal TextBlock InWell => WellProbe;

    /// <summary>The same Silver on a faceplate, which follows the shift; without it the well's check could pass
    /// on text that never followed any theme.</summary>
    internal TextBlock OnFace => FaceProbe;

    /// <summary>The body-text swatch on the same faceplate: Silver named on the element itself, not by a style.</summary>
    internal Border Swatch => SwatchProbe;

    /// <summary>A colour from the application's own theme dictionaries, for the shift measurement.</summary>
    internal Border FromApp => AppThemeProbe;

    /// <summary>A colour from this control's own theme dictionaries, for the shift measurement.</summary>
    internal Border FromElement => LocalThemeProbe;

    /// <summary>A colour from theme dictionaries in a file merged straight into App.xaml.</summary>
    internal Border FromMergedFile => MergedThemeProbe;

    /// <summary>A colour from a file merged into App.xaml's own theme dictionary for the shift.</summary>
    internal Border FromThemeFile => ThemeFileProbe;
}
