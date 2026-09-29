using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SysMonitor.Core.Services.Monitors;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// One <see cref="CpuMonitor"/> is shared by the whole app, and its callers run on their own timers and their
/// own threads: the page on screen, the FPS overlay's 500 ms loop, the tray tooltip, the history recorder.
/// The total-usage baseline they read through used to be four fields with no lock, so two readers arriving
/// together could each take half of the other's update.
/// <para>
/// These read the real kernel counters, from many threads at once. The shared path is locked and the
/// long-lived consumers now hold samplers of their own (<see cref="ICpuMonitor.CreateSampler"/>), so the
/// assertion is the one a caller relies on: nothing throws and every reading is a percentage.
/// </para>
/// </summary>
public class CpuMonitorConcurrencyTests : IDisposable
{
    private readonly CpuMonitor _monitor = new(Mock.Of<ILogger<CpuMonitor>>());

    public void Dispose() => _monitor.Dispose();

    [Fact]
    public async Task ManyThreadsReadingTheSharedPathAtOnce_StayInRange()
    {
        var readers = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            var readings = new List<double>();
            for (var i = 0; i < 20; i++)
            {
                readings.Add(await _monitor.GetUsagePercentAsync());
                readings.Add((await _monitor.GetCpuInfoAsync()).UsagePercent);
            }

            return readings;
        }));

        var readings = (await Task.WhenAll(readers)).SelectMany(batch => batch).ToList();

        readings.Should().HaveCount(32 * 20 * 2);
        readings.Should().AllSatisfy(usage => usage.Should().BeInRange(0, 100));
    }

    [Fact]
    public async Task SamplersHandedOutTogether_EachReadIndependently()
    {
        var samplers = Enumerable.Range(0, 16).Select(_ => _monitor.CreateSampler()).ToList();

        await Task.Delay(100);

        var readings = await Task.WhenAll(samplers.Select(sampler => Task.Run(sampler.Sample)));

        readings.Should().AllSatisfy(usage => usage.Should().BeInRange(0, 100));
        samplers.Should().OnlyHaveUniqueItems("each consumer gets a baseline of its own, not a shared one");
    }
}
