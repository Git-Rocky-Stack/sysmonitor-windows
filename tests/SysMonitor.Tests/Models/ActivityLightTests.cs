using FluentAssertions;
using SysMonitor.Core.Models;
using Xunit;

namespace SysMonitor.Tests.Models;

/// <summary>
/// The ACT light that sits on a process's progress meter: dark until the work is shown, a strike of quick
/// flashes the moment it begins, an irregular drive-activity flicker while it runs, and held lit once the meter
/// is full. It is tied to the meter, so these are read off the meter's own numbers.
/// </summary>
public class ActivityLightTests
{
    [Theory]
    [InlineData(false, false, 40)]
    [InlineData(false, true, 40)]
    public void AMeterThatIsNotShownLeavesTheLightDark(bool showing, bool indeterminate, double value) =>
        ActivityLight.PhaseOf(showing, indeterminate, value, 0, 100).Should().Be(ActivityPhase.Dark);

    [Fact]
    public void WorkWithNoExtentKeepsTheLightBusy() =>
        ActivityLight.PhaseOf(true, true, double.NaN, 0, 100).Should().Be(ActivityPhase.Busy,
            "an indeterminate meter is work under way, whatever its value says");

    [Theory]
    [InlineData(0)]
    [InlineData(37.5)]
    [InlineData(99.9)]
    public void WorkUnderWayIsBusyFromItsFirstMoment(double value) =>
        ActivityLight.PhaseOf(true, false, value, 0, 100).Should().Be(ActivityPhase.Busy,
            "a process meter is shown when the work begins, at nought, and that is when the light must start");

    [Theory]
    [InlineData(100)]
    [InlineData(140)]
    public void AFullMeterHoldsTheLightLit(double value) =>
        ActivityLight.PhaseOf(true, false, value, 0, 100).Should().Be(ActivityPhase.Done);

    [Theory]
    [InlineData(double.NaN, 0, 100)]
    [InlineData(double.PositiveInfinity, 0, 100)]
    [InlineData(50, 100, 100)]
    public void AReadingThatMeansNothingLightsNothing(double value, double minimum, double maximum) =>
        ActivityLight.PhaseOf(true, false, value, minimum, maximum).Should().Be(ActivityPhase.Dark);

    [Theory]
    [InlineData(ActivityPhase.Dark, ActivityPhase.Busy, 0, 0, true)]
    [InlineData(ActivityPhase.Done, ActivityPhase.Busy, 100, 0, true)]
    [InlineData(ActivityPhase.Busy, ActivityPhase.Busy, 80, 2, true)]
    [InlineData(ActivityPhase.Busy, ActivityPhase.Busy, 10, 20, false)]
    [InlineData(ActivityPhase.Busy, ActivityPhase.Done, 90, 100, false)]
    [InlineData(ActivityPhase.Busy, ActivityPhase.Dark, 90, 90, false)]
    public void TheLightStrikesWhenAProcessBegins(ActivityPhase from, ActivityPhase to, double before, double after,
        bool strikes) =>
        ActivityLight.Strikes(from, to, before, after).Should().Be(strikes,
            "a strike marks a beginning: the light coming up on work, or a meter that fell back to start a new run");

    [Fact]
    public void TheStrikeIsThreeQuickFlashesThatEndLit()
    {
        var strike = ActivityLight.Strike;

        strike.Count(blink => blink.Lit).Should().Be(3);
        strike[0].Should().Be(new ActivityBlink(TimeSpan.Zero, true));
        strike[^1].Lit.Should().BeTrue("the strike hands over to the flicker on a lit light, not a dark one");
        strike[^1].At.Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(400), "a strike is a moment, not a show");
        ActivityLight.StrikeDuration.Should().BeGreaterThan(strike[^1].At);
    }

    [Fact]
    public void TheFlickerIsADriveLightNotAMetronome()
    {
        var cadence = ActivityLight.Cadence;
        var intervals = Intervals(cadence, ActivityLight.CadenceDuration);

        cadence[0].Should().Be(new ActivityBlink(TimeSpan.Zero, true));
        cadence.Zip(cadence.Skip(1)).Should().OnlyContain(pair => pair.First.Lit != pair.Second.Lit,
            "every key turns the light the other way");
        intervals.Distinct().Count().Should().BeGreaterThan(2, "an evenly spaced blink reads as a warning, not as work");
        intervals.Should().OnlyContain(gap => gap >= TimeSpan.FromMilliseconds(40),
            "a flash shorter than about three frames at 60 Hz never reaches the screen");
        ActivityLight.CadenceDuration.Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(1200));
    }

    [Fact]
    public void TheFlickerIsLitMoreOftenThanNot()
    {
        var cadence = ActivityLight.Cadence;
        var intervals = Intervals(cadence, ActivityLight.CadenceDuration);
        var lit = cadence.Zip(intervals).Where(pair => pair.First.Lit).Sum(pair => pair.Second.TotalMilliseconds);

        (lit / ActivityLight.CadenceDuration.TotalMilliseconds).Should().BeInRange(0.5, 0.8,
            "a working light reads as on with drop-outs, which is what a drive's ACT light does");
    }

    private static List<TimeSpan> Intervals(IReadOnlyList<ActivityBlink> keys, TimeSpan end) =>
        keys.Select((key, i) => (i + 1 < keys.Count ? keys[i + 1].At : end) - key.At).ToList();
}
