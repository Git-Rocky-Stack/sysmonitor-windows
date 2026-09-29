using FluentAssertions;
using SysMonitor.Core.Helpers;
using Xunit;

namespace SysMonitor.Tests.Helpers;

/// <summary>
/// <see cref="PanelSerial"/> ports System-X's <c>serialFor</c>, so a module both apps share carries the same
/// digits in each. Every expected digit below is what System-X's own function returns for that name, run
/// verbatim under Node from system-x-app@eaba14b src/components/console/Faceplate.tsx, with its SX prefix read
/// as STX. The long name wraps the 32-bit hash many times over and the accented one is past ASCII, which is
/// where a port that used signed or 64-bit arithmetic, or bytes instead of UTF-16 code units, would part ways.
/// </summary>
public class PanelSerialTests
{
    [Theory]
    [InlineData("", "S/N STX-0000-00")]
    [InlineData("A", "S/N STX-0065-00")]
    [InlineData("DASH", "S/N STX-0898-31")]
    [InlineData("CPU", "S/N STX-6952-01")]
    [InlineData("HEALTH CHECK", "S/N STX-5156-21")]
    [InlineData("PDF EDIT", "S/N STX-8008-32")]
    [InlineData("NET MAP", "S/N STX-6969-59")]
    [InlineData("LARGE FILES", "S/N STX-9698-67")]
    [InlineData("palette", "S/N STX-6443-45")]
    [InlineData("led-vocabulary", "S/N STX-6824-27")]
    [InlineData("Night Ops", "S/N STX-2986-93")]
    [InlineData("Day Shift", "S/N STX-6574-75")]
    [InlineData("Thémes", "S/N STX-4166-68")]
    [InlineData("A really long module name that wraps the hash several times over", "S/N STX-2916-35")]
    public void ASerialIsTheOneSystemXStampsForTheSameName(string module, string expected)
    {
        PanelSerial.For(module).Should().Be(expected);
    }

    [Fact]
    public void AModuleKeepsItsSerial()
    {
        PanelSerial.For("MEMORY").Should().Be(PanelSerial.For("MEMORY"),
            "a serial that changes between visits is decoration, not identity");
    }

    [Fact]
    public void DifferentModulesCarryDifferentSerials()
    {
        PanelSerial.For("CPU").Should().NotBe(PanelSerial.For("GPU"));
    }
}
