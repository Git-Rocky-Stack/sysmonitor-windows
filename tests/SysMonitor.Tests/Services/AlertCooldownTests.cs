using System.Text.Json;
using FluentAssertions;
using Moq;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Alerts;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The cooldown is the whole reason the notification tray is usable: a metric sitting on its threshold
/// flickers across it every few seconds, and each crossing used to count as news. The gate was
/// <c>state.IsActive &amp;&amp; elapsed &lt; CooldownPeriod</c>, and the automatic clear-down set
/// <c>IsActive = false</c> without touching <c>LastTriggered</c> — so every dip below the threshold re-armed
/// the alert and a 5-second poll produced a toast every 10 seconds against a documented 5-minute cooldown.
///
/// The second half of these tests is about the clock. The gate read <c>DateTime.Now</c>, which goes
/// backwards once a year; a negative elapsed time is less than the cooldown, so every alert on the machine
/// was suppressed for the whole repeated hour.
/// </summary>
public class AlertCooldownTests : IDisposable
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly TempDirectory _temp = new("alerts");
    private readonly TestClock _clock = new(Noon);
    private readonly Mock<ITemperatureMonitor> _temperature = new();
    private readonly Mock<IMemoryMonitor> _memory = new();
    private readonly Mock<IBatteryMonitor> _battery = new();

    private double _cpuTemperature;

    public void Dispose() => _temp.Dispose();

    public AlertCooldownTests()
    {
        _temperature.Setup(t => t.GetCpuTemperatureAsync()).ReturnsAsync(() => _cpuTemperature);
        _temperature.Setup(t => t.GetGpuTemperatureAsync()).ReturnsAsync(0);

        // Nothing else is near a threshold, so these never speak up.
        _memory.Setup(m => m.GetMemoryInfoAsync()).ReturnsAsync(new MemoryInfo { UsagePercent = 10 });
        _battery.Setup(b => b.GetBatteryInfoAsync()).ReturnsAsync((BatteryInfo?)null);
    }

    // ---------------------------------------------------------------- flapping

    [Fact]
    public async Task AMetricFlappingAcrossItsThreshold_IsReportedOnce()
    {
        var (service, fired) = Service();

        // Six polls five seconds apart, alternating over and under 75 °C: exactly what a CPU sitting on
        // its threshold does.
        foreach (var temperature in new double[] { 80, 70, 80, 70, 80, 70 })
        {
            _cpuTemperature = temperature;
            await service.CheckThresholdsAsync();
            _clock.Advance(TimeSpan.FromSeconds(5));
        }

        fired.Should().ContainSingle("the cooldown is on the alert, not on the condition being unbroken");
    }

    [Fact]
    public async Task AConditionStayingOverTheThreshold_IsReportedOnce()
    {
        var (service, fired) = Service();

        for (var poll = 0; poll < 6; poll++)
        {
            _cpuTemperature = 80;
            await service.CheckThresholdsAsync();
            _clock.Advance(TimeSpan.FromSeconds(5));
        }

        fired.Should().ContainSingle();
    }

    [Fact]
    public async Task OnceTheCooldownHasRunOut_TheAlertIsReportedAgain()
    {
        var (service, fired) = Service();

        _cpuTemperature = 80;
        await service.CheckThresholdsAsync();
        fired.Should().ContainSingle("the test is worthless if the first alert never fired");

        _clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await service.CheckThresholdsAsync();

        fired.Should().HaveCount(2, "a condition still broken after the cooldown is news again");
    }

    [Fact]
    public async Task ClearingTheAlertByHand_ReportsItAgainStraightAway()
    {
        var (service, fired) = Service();

        _cpuTemperature = 80;
        await service.CheckThresholdsAsync();
        fired.Should().ContainSingle();

        // ClearAlert is documented as resetting the cooldown; the automatic clear-down is not.
        service.ClearAlert(AlertType.CpuTempWarning);
        await service.CheckThresholdsAsync();

        fired.Should().HaveCount(2);
    }

    // ---------------------------------------------------------------- the clock going backwards

    [Fact]
    public async Task TheClockGoingBackwards_DoesNotSilenceAlerts()
    {
        var (service, fired) = Service();

        _cpuTemperature = 80;
        await service.CheckThresholdsAsync();
        fired.Should().ContainSingle();

        // The hour that happens twice, or an operator correcting the clock. An alert must not be held back
        // because the time it was raised at is now in the future.
        _clock.Rewind(TimeSpan.FromHours(1));
        await service.CheckThresholdsAsync();

        fired.Should().HaveCount(2, "when in doubt, say it: a missed critical temperature costs more than a repeat");
    }

    [Fact]
    public async Task TheCooldownIsMeasuredOnAClockThatDaylightSavingDoesNotMove()
    {
        var (service, fired) = Service();

        _cpuTemperature = 80;
        await service.CheckThresholdsAsync();
        fired.Should().ContainSingle();

        // The autumn fall-back, as local time sees it: the clock reads an hour earlier than it did, while
        // only two minutes of real time have passed. On local time the elapsed value went negative and
        // suppressed the alert for the whole repeated hour; on UTC it is two minutes, still inside the
        // cooldown, and the alert is held back for the right reason.
        _clock.Advance(TimeSpan.FromMinutes(2));
        await service.CheckThresholdsAsync();

        fired.Should().ContainSingle("two minutes is two minutes wherever the machine thinks it is");
    }

    // ---------------------------------------------------------------- helpers

    private (AlertService Service, List<AlertNotification> Fired) Service()
    {
        var settings = Path.Combine(_temp.Path, "settings.json");
        File.WriteAllText(settings, JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["ShowNotifications"] = true,
            ["EnableTempAlerts"] = true,
            ["CpuTempWarning"] = 75,
            ["CpuTempCritical"] = 90,
        }));

        var service = new AlertService(
            Mock.Of<ICpuMonitor>(), _memory.Object, _temperature.Object, _battery.Object,
            logger: null, settingsPath: settings, timeProvider: _clock);

        var fired = new List<AlertNotification>();
        service.AlertTriggered += (_, notification) => fired.Add(notification);
        return (service, fired);
    }
}
