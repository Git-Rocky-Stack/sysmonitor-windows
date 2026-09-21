using FluentAssertions;
using SysMonitor.Core.Services.Monitors;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// <c>GetSystemPowerStatus</c> has two ways of saying "I do not know", and both of them used to be read as
/// facts.
/// <para>
/// <c>BatteryFlag</c> 255 means the status is unknown; only 128 means there is no battery. A desktop whose
/// firmware answers 255 was therefore reported as having a battery.
/// </para>
/// <para>
/// <c>BatteryLifePercent</c> 255 means the charge is unknown. The reading was
/// <c>percent &lt;= 100 ? percent : 0</c>, so "unknown" became "0%" — and the alert service compares the
/// charge against a critical threshold. The user of a machine with no readable battery got
/// "Battery Critical! Battery is at 0% - plug in immediately!" on a desktop.
/// </para>
/// </summary>
public class BatteryStatusReadingTests
{
    private const byte NoBattery = 128;
    private const byte Unknown = 255;
    private const byte Charging = 8;

    [Fact]
    public void NoSystemBattery_IsNoBattery()
    {
        BatteryStatusReading.Read(Status(batteryFlag: NoBattery, percent: Unknown))
            .Should().BeNull("a machine with no battery has nothing to report");
    }

    [Fact]
    public void UnknownStatusAndUnknownCharge_IsNoBattery()
    {
        BatteryStatusReading.Read(Status(batteryFlag: Unknown, percent: Unknown))
            .Should().BeNull("nothing is known, and a battery reported at 0% would raise a critical alert");
    }

    [Fact]
    public void UnknownStatusButAReadableCharge_IsABatteryAtThatCharge()
    {
        var battery = BatteryStatusReading.Read(Status(batteryFlag: Unknown, percent: 64));

        battery.Should().NotBeNull();
        battery!.IsPresent.Should().BeTrue("a charge level is only reported for a battery that exists");
        battery.ChargePercent.Should().Be(64);
    }

    [Fact]
    public void AKnownStatusButUnknownCharge_IsNotReportedAsEmpty()
    {
        var battery = BatteryStatusReading.Read(Status(batteryFlag: 1, percent: Unknown));

        battery.Should().BeNull(
            "0% is what the old reading produced here, and 0% is what raises Battery Critical");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void AReadableCharge_IsReportedAsItIs(byte percent)
    {
        var battery = BatteryStatusReading.Read(Status(batteryFlag: 1, percent: percent));

        battery.Should().NotBeNull();
        battery!.ChargePercent.Should().Be(percent);
    }

    [Fact]
    public void TheChargingFlag_IsRead()
    {
        BatteryStatusReading.Read(Status(batteryFlag: Charging, percent: 40))!.IsCharging.Should().BeTrue();
        BatteryStatusReading.Read(Status(batteryFlag: 1, percent: 40))!.IsCharging.Should().BeFalse();
    }

    [Fact]
    public void MainsPower_IsRead()
    {
        BatteryStatusReading.Read(Status(acLineStatus: 1, batteryFlag: 1, percent: 40))!
            .IsPluggedIn.Should().BeTrue();
        BatteryStatusReading.Read(Status(acLineStatus: 0, batteryFlag: 1, percent: 40))!
            .IsPluggedIn.Should().BeFalse();
    }

    [Fact]
    public void ARuntimeWindowsCouldNotEstimate_IsNotReportedAsZeroSeconds()
    {
        // GetSystemPowerStatus uses -1 for "unknown".
        var battery = BatteryStatusReading.Read(Status(batteryFlag: 1, percent: 40, runtimeSeconds: -1));

        battery!.EstimatedRuntime.Should().Be(TimeSpan.Zero, "zero is how this app already says 'no estimate'");
    }

    [Fact]
    public void ARuntimeWindowsDidEstimate_IsCarriedThrough()
    {
        var battery = BatteryStatusReading.Read(Status(batteryFlag: 1, percent: 40, runtimeSeconds: 3600));

        battery!.EstimatedRuntime.Should().Be(TimeSpan.FromHours(1));
    }

    private static BatteryPowerStatus Status(
        byte batteryFlag, byte percent, byte acLineStatus = 0, int runtimeSeconds = -1) =>
        new(acLineStatus, batteryFlag, percent, runtimeSeconds);
}
