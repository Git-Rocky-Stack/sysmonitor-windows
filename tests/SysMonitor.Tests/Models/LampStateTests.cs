using FluentAssertions;
using SysMonitor.Core.Models;
using Xunit;

namespace SysMonitor.Tests.Models;

/// <summary>
/// <see cref="LampStates"/> grades a reading for every page that shows one on a lamp, a rail or a chip. The
/// thresholds are System-X's defaults (<c>usageState</c> and <c>scoreState</c>), and each boundary is tested
/// on both sides, because a boundary that moves by one is a lamp that changes colour at the wrong reading on
/// every page at once.
/// </summary>
public class LampStateTests
{
    [Theory]
    [InlineData(0, LampState.Go)]
    [InlineData(74.9, LampState.Go)]
    [InlineData(75, LampState.Hold)]
    [InlineData(89.9, LampState.Hold)]
    [InlineData(90, LampState.NoGo)]
    [InlineData(100, LampState.NoGo)]
    public void AUsageReadingIsGradedAtSeventyFiveAndNinety(double percent, LampState expected)
    {
        LampStates.Usage(percent).Should().Be(expected);
    }

    [Theory]
    [InlineData(100, LampState.Go)]
    [InlineData(80, LampState.Go)]
    [InlineData(79.9, LampState.Hold)]
    [InlineData(60, LampState.Hold)]
    [InlineData(59.9, LampState.NoGo)]
    [InlineData(0, LampState.NoGo)]
    public void AScoreIsGradedAtEightyAndSixty(double score, LampState expected)
    {
        LampStates.Score(score).Should().Be(expected);
    }

    [Fact]
    public void APageKeepsItsOwnThresholds()
    {
        // The Dashboard's memory lamp, say, turns at the user's own Memory threshold, not at 75.
        LampStates.Usage(70, critical: 85, warning: 65).Should().Be(LampState.Hold);
        LampStates.Usage(86, critical: 85, warning: 65).Should().Be(LampState.NoGo);
        LampStates.Score(55, good: 50, moderate: 30).Should().Be(LampState.Go);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void AMissingReadingLightsNothing(double reading)
    {
        // System-X's own functions would call NaN healthy (NaN >= 90 is false all the way down to GO); a
        // sensor that reported nothing is not a green lamp.
        LampStates.Usage(reading).Should().Be(LampState.Off);
        LampStates.Score(reading).Should().Be(LampState.Off);
    }
}
