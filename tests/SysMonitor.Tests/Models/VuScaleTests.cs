using FluentAssertions;
using SysMonitor.Core.Models;
using Xunit;

namespace SysMonitor.Tests.Models;

/// <summary>
/// <see cref="VuScale"/> is the arithmetic behind every VU meter: how many segments a reading lights and which
/// colour each one is. The cases are System-X's own (src/components/console/Vu.tsx), including the rounding it
/// gets from JavaScript's <c>Math.round</c>, which takes a half up where .NET's default would take it to even.
/// </summary>
public class VuScaleTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(38, 6)]        // 6.08
    [InlineData(72, 12)]       // 11.52
    [InlineData(96, 15)]       // 15.36
    [InlineData(100, 16)]
    [InlineData(40.625, 7)]    // exactly 6.5: half up, where banker's rounding would give 6
    [InlineData(-20, 0)]       // clamped
    [InlineData(250, 16)]      // clamped
    public void AReadingLightsItsShareOfSixteenSegments(double value, int lit)
    {
        VuScale.Lit(value, 0, 100, 16).Should().Be(lit);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void AMissingReadingLightsNothing(double value)
    {
        VuScale.Lit(value, 0, 100, 16).Should().Be(0, "a meter lit over a reading that is not there would show one");
    }

    [Fact]
    public void ARangeWithNoWidthLightsNothing()
    {
        VuScale.Lit(50, 10, 10, 16).Should().Be(0);
        VuScale.Lit(50, 0, 100, 0).Should().Be(0);
    }

    [Fact]
    public void AnotherRangeIsScaledToIt()
    {
        VuScale.Lit(3, 0, 4, 12).Should().Be(9);
        VuScale.Lit(75, 50, 100, 10).Should().Be(5);
    }

    [Fact]
    public void TheWarmZonesArePackedIntoTheTopOfTheScale()
    {
        var zones = Enumerable.Range(0, 16).Select(index => VuScale.ZoneOf(index, 16)).ToList();

        // 16 segments: positions 1/16 to 16/16. Past .6 is the 10th segment, past .8 the 13th, past .9 the 15th.
        zones.Take(9).Should().AllBeEquivalentTo(VuZone.Go);
        zones.Skip(9).Take(3).Should().AllBeEquivalentTo(VuZone.Hold);
        zones.Skip(12).Take(2).Should().AllBeEquivalentTo(VuZone.Orange);
        zones.Skip(14).Should().AllBeEquivalentTo(VuZone.Red);
    }

    [Fact]
    public void ASegmentOnABoundaryStaysInTheCoolerZone()
    {
        // Exactly .6, .8 and .9 of ten segments: System-X tests "greater than", so each stays below.
        VuScale.ZoneOf(5, 10).Should().Be(VuZone.Go);
        VuScale.ZoneOf(7, 10).Should().Be(VuZone.Hold);
        VuScale.ZoneOf(8, 10).Should().Be(VuZone.Orange);
        VuScale.ZoneOf(9, 10).Should().Be(VuZone.Red);
    }
}
