using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SysMonitor.Core.Services.Monitors;
using Xunit;

namespace SysMonitor.Tests.Services;

public class CpuMonitorTests : IDisposable
{
    private readonly CpuMonitor _cpuMonitor = new(Mock.Of<ILogger<CpuMonitor>>());

    public void Dispose() => _cpuMonitor.Dispose();

    [Fact]
    public async Task GetCpuInfoAsync_ReturnsValidInfo()
    {
        var info = await _cpuMonitor.GetCpuInfoAsync();

        info.Should().NotBeNull();
        info.LogicalProcessors.Should().Be(Environment.ProcessorCount);
        info.Cores.Should().BeGreaterThan(0).And.BeLessOrEqualTo(Environment.ProcessorCount);
        info.Name.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A dead monitor reads zero, and so does an idle machine, so a reading in range proves nothing on its
    /// own - the reason this test used to pass against a monitor hard-wired to 0%. Here the test makes the
    /// machine busy itself and then insists the monitor noticed.
    /// </summary>
    [Fact]
    public async Task GetUsagePercentAsync_NoticesWorkTheMachineIsDoing()
    {
        // The first reading only starts the clock: usage is the difference between two samples.
        await _cpuMonitor.GetUsagePercentAsync();

        BurnCpu(TimeSpan.FromMilliseconds(400));
        var usage = await _cpuMonitor.GetUsagePercentAsync();

        usage.Should().BeGreaterThan(0, "a core was busy for the whole sample");
        usage.Should().BeLessOrEqualTo(100);
    }

    [Fact]
    public async Task GetUsagePercentAsync_StaysWithinRange()
    {
        for (var reading = 0; reading < 5; reading++)
        {
            // Far enough apart that the kernel's counters have moved; back to back they do not, and the
            // reading is a flat zero that no arithmetic mistake in between could ever show up in.
            await Task.Delay(50);

            var usage = await _cpuMonitor.GetUsagePercentAsync();
            usage.Should().BeInRange(0, 100);
        }
    }

    [Fact]
    public async Task GetCoreUsagesAsync_ReturnsUsageForEachCore()
    {
        var usages = await _cpuMonitor.GetCoreUsagesAsync();

        usages.Should().HaveCount(Environment.ProcessorCount);
        usages.Should().AllSatisfy(usage => usage.Should().BeInRange(0, 100));
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var disposeTwice = () =>
        {
            _cpuMonitor.Dispose();
            _cpuMonitor.Dispose();
        };

        disposeTwice.Should().NotThrow();
    }

    /// <summary>Keeps one core busy for a while, so there is something for the monitor to report.</summary>
    private static void BurnCpu(TimeSpan duration)
    {
        var stopwatch = Stopwatch.StartNew();
        var spin = 0L;
        while (stopwatch.Elapsed < duration)
        {
            // Enough arithmetic that the loop cannot be optimised into a sleep.
            spin += stopwatch.ElapsedTicks % 7;
        }

        GC.KeepAlive(spin);
    }
}
