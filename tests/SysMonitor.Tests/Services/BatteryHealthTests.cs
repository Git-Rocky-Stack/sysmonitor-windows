using FluentAssertions;
using SysMonitor.Core.Services.Monitors;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Battery health is how much of its original capacity the battery can still hold. It is not the charge
/// level: a healthy battery at 15% used to be shown as being in critical health, because the charge
/// percentage was passed to the function that names the health.
/// <para>
/// Windows reports the two capacity figures on some machines and not others - on this one Win32_Battery
/// leaves both empty - so "not known" is a normal answer and has to be said rather than guessed.
/// </para>
/// </summary>
public class BatteryHealthTests
{
    [Theory]
    [InlineData(50.0, 50.0, "Good")]     // as new
    [InlineData(50.0, 45.0, "Good")]     // 90%
    [InlineData(50.0, 40.0, "Good")]     // 80%, the edge
    [InlineData(50.0, 39.0, "Fair")]     // 78%
    [InlineData(50.0, 30.0, "Fair")]     // 60%, the edge
    [InlineData(50.0, 29.0, "Worn")]     // 58%
    [InlineData(50.0, 20.0, "Worn")]     // 40%, the edge
    [InlineData(50.0, 19.0, "Poor")]     // 38%
    [InlineData(50.0, 5.0, "Poor")]      // 10%
    public void HealthFromCapacity_ComparesWhatItHoldsWithWhatItWasBuiltToHold(
        double designWattHours, double fullChargeWattHours, string expected)
    {
        BatteryMonitor.HealthFromCapacity(designWattHours, fullChargeWattHours).Should().Be(expected);
    }

    [Theory]
    [InlineData(0.0, 45.0)]
    [InlineData(50.0, 0.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(-1.0, 45.0)]
    public void HealthFromCapacity_SaysSoWhenWindowsDoesNotReportTheCapacity(double design, double full)
    {
        BatteryMonitor.HealthFromCapacity(design, full).Should().Be(BatteryMonitor.UnknownHealth);
    }

    [Fact]
    public void HealthFromCapacity_DoesNotCallABatteryThatOverchargesBetterThanNew()
    {
        // A battery that reads above its design capacity is measurement noise, not extra health.
        BatteryMonitor.HealthFromCapacity(50.0, 60.0).Should().Be("Good");
    }

    [Fact]
    public void HealthFromCapacity_IsNotTheChargeLevel()
    {
        // The defect this replaced: 15 was a charge percentage, and it came back as critical health.
        var healthyBatteryAtFifteenPercent = BatteryMonitor.HealthFromCapacity(
            designWattHours: 50.0, fullChargeWattHours: 48.0);

        healthyBatteryAtFifteenPercent.Should().Be("Good", "the charge level says nothing about wear");
    }
}
