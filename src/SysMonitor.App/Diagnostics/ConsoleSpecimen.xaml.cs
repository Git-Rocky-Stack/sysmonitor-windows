using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.Controls.Instruments;

namespace SysMonitor.App.Diagnostics;

/// <summary>Every console instrument in every state, for the UI smoke run to show in both shifts.</summary>
public sealed partial class ConsoleSpecimen : UserControl
{
    public ConsoleSpecimen()
    {
        InitializeComponent();

        // A cancel cap and an action only show with a command to run; these have one that does nothing.
        CancellableBusy.CancelCommand = Idle;
        StandbyWithAction.ActionCommand = Idle;
    }

    /// <summary>The command the specimen's caps run: nothing. They are there to be drawn.</summary>
    private static ICommand Idle { get; } = new RelayCommand(() => { });

    /// <summary>A line of Silver text inside a well: Night Ops silver in both shifts, or the well leaks.</summary>
    internal TextBlock InWell => WellProbe;

    /// <summary>The same Silver on a faceplate, which follows the shift; without it the well's check could pass
    /// on text that never followed any theme.</summary>
    internal TextBlock OnFace => FaceProbe;

    /// <summary>The body-text swatch on the same faceplate: Silver named on the element itself, not by a style.</summary>
    internal Border Swatch => SwatchProbe;

    /// <summary>The view header, with a lamp in its status and a cap in its actions.</summary>
    internal ViewHeader Header => HeaderProbe;

    /// <summary>A NO-GO banner by default, with a message.</summary>
    internal Banner Fault => BannerProbe;

    /// <summary>A busy panel that knows its extent: 42.5%, shown as 43%.</summary>
    internal BusyPanel Progress => BusyProbe;
}
