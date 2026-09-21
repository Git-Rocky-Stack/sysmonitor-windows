using FluentAssertions;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// One <see cref="TemperatureMonitor"/> is shared by the whole app — the 5-second dashboard timer, the
/// 30-second history timer, the alert check and the overlay loop all hold the same singleton. Every read
/// calls <c>IHardware.Update()</c>, which walks and rewrites the sensor tree in place, and none of them used
/// to take a lock.
///
/// <para>
/// <b>What this proves depends on the machine.</b> LibreHardwareMonitor needs administrator rights to open
/// the kernel driver it reads sensors through. Without them there are no sensors and these tests exercise
/// the empty path only — still worth having, because that path is also concurrent, but it is not a proof of
/// thread safety. On a machine where the driver opens, the same tests drive the real sensor tree from a
/// dozen threads at once. The count is asserted on so a reader can see which of the two happened.
/// </para>
/// </summary>
[Collection(SerialCollection.Name)]
public class TemperatureMonitorConcurrencyTests
{
    [Fact]
    public async Task ReadingEveryKindOfSensorAtOnce_DoesNotThrow()
    {
        using var monitor = new TemperatureMonitor();
        await monitor.InitializeAsync();

        var readers = new List<Func<Task>>();
        for (var round = 0; round < 6; round++)
        {
            readers.Add(() => monitor.GetAllTemperaturesAsync());
            readers.Add(() => monitor.GetAllFanSpeedsAsync());
            readers.Add(() => monitor.GetAllPowerReadingsAsync());
            readers.Add(() => monitor.GetAllLoadSensorsAsync());
            readers.Add(() => monitor.GetCpuTemperatureAsync());
            readers.Add(() => monitor.GetGpuTemperatureAsync());
            readers.Add(() => monitor.GetFrameRateAsync());
            readers.Add(() => monitor.GetTotalSystemPowerAsync());
        }

        var act = async () => await Task.WhenAll(readers.Select(read => Task.Run(read)));

        await act.Should().NotThrowAsync("the sensor tree is rewritten in place by every one of these");
    }

    [Fact]
    public async Task OpeningTheHardwareFromSeveralThreadsAtOnce_LeavesItUsable()
    {
        using var monitor = new TemperatureMonitor();

        // Every caller that reads a temperature initialises first, and several of them start together
        // when the app launches. Two that both got past an unguarded check would each open the hardware,
        // and the second would replace the first - leaking the driver handle the first one holds.
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => monitor.InitializeAsync())));

        var temperatures = await monitor.GetAllTemperaturesAsync();
        temperatures.Should().NotBeNull("the monitor is still usable after being opened from eight threads");
    }

    [Fact]
    public async Task ReadingAfterDispose_AnswersEmptyRatherThanThrowing()
    {
        var monitor = new TemperatureMonitor();
        await monitor.InitializeAsync();
        monitor.Dispose();

        // Shutdown disposes this while a timer tick may still be in flight.
        var temperatures = await monitor.GetAllTemperaturesAsync();

        temperatures.Should().BeEmpty("the hardware is closed, so there is nothing to report");
    }

    [Fact]
    public async Task DisposingTwice_DoesNotThrow()
    {
        var monitor = new TemperatureMonitor();
        await monitor.InitializeAsync();

        var act = () => { monitor.Dispose(); monitor.Dispose(); };

        act.Should().NotThrow("the host disposes the singleton it built, and the page may have too");
    }
}
