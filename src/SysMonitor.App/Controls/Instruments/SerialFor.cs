using Microsoft.UI.Xaml.Markup;
using SysMonitor.Core.Helpers;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// A panel's serial, written where it is used: <c>Serial="{instruments:SerialFor Module=PALETTE}"</c> stamps the
/// serial <see cref="PanelSerial"/> derives from the name, as System-X's pages write
/// <c>serial={serialFor('palette')}</c>. A serial typed out as a literal would drift from its name the first time
/// either changed.
/// </summary>
[MarkupExtensionReturnType(ReturnType = typeof(string))]
public sealed class SerialFor : MarkupExtension
{
    /// <summary>The name the serial is derived from.</summary>
    public string Module { get; set; } = string.Empty;

    protected override object ProvideValue() => PanelSerial.For(Module);
}
