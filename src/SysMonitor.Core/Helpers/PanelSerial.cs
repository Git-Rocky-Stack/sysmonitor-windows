using System.Globalization;

namespace SysMonitor.Core.Helpers;

/// <summary>
/// The serial number stamped on a console panel's stripe, such as <c>S/N STX-0898-31</c>.
/// <para>
/// It is derived from the panel's module name, so a panel carries the same number every time it is drawn: a
/// serial that changed from one visit to the next would be set dressing, and the design forbids set dressing.
/// The arithmetic is System-X's <c>serialFor</c> (src/components/console/Faceplate.tsx) exactly - a 32-bit
/// rolling hash of the name's UTF-16 code units - so a module shared by both apps carries the same digits in
/// each. Only the prefix differs: STX for this product, where System-X stamps SX.
/// </para>
/// </summary>
public static class PanelSerial
{
    public static string For(string module)
    {
        ArgumentNullException.ThrowIfNull(module);

        // JavaScript's (hash * 31 + code) >>> 0 is this multiplication wrapping at 32 bits.
        uint hash = 0;
        foreach (char code in module)
        {
            hash = unchecked(hash * 31 + code);
        }

        var block = (hash % 10000).ToString("D4", CultureInfo.InvariantCulture);
        var unit = ((hash >> 16) % 100).ToString("D2", CultureInfo.InvariantCulture);
        return $"S/N STX-{block}-{unit}";
    }
}
