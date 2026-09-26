using System.Diagnostics;
using FluentAssertions;
using SysMonitor.Core.Services.Monitors;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// <see cref="CpuSampler"/> is what gives each consumer a CPU reading measured over its own interval. These
/// tests drive it with counters and a clock they control, so every expected percentage is arithmetic a reader
/// can check by hand - and they run on any machine, not only one whose kernel can be asked.
/// <para>
/// The bug they pin down: one shared baseline let whichever caller asked last decide the interval everybody
/// else measured. <see cref="TwoSamplersEachMeasureTheirOwnInterval"/> is that bug stated as a test.
/// </para>
/// </summary>
public class CpuSamplerTests
{
    /// <summary>100 ns ticks, the unit GetSystemTimes reports in; the exact scale does not matter to a ratio.</summary>
    private sealed class FakeKernel
    {
        private readonly object _gate = new();
        private long _idle, _kernel, _user;

        public int Reads { get; private set; }

        public bool Reporting { get; set; } = true;

        /// <summary>Advance the machine: <paramref name="busy"/> ticks spent working, <paramref name="idle"/> idle.</summary>
        public void Run(long busy, long idle)
        {
            lock (_gate)
            {
                // Kernel time includes idle time, which is how GetSystemTimes reports it.
                _idle += idle;
                _kernel += idle + busy / 2;
                _user += busy - busy / 2;
            }
        }

        public SystemTimes? Read()
        {
            lock (_gate)
            {
                Reads++;
                return Reporting ? new SystemTimes(_idle, _kernel, _user) : null;
            }
        }
    }

    private sealed class FakeClock
    {
        private long _now = 1_000_000;

        public void Advance(TimeSpan by) => Interlocked.Add(ref _now, (long)(by.TotalSeconds * Stopwatch.Frequency));

        public long Now() => Interlocked.Read(ref _now);
    }

    private static CpuSampler Sampler(FakeKernel kernel, FakeClock clock, TimeSpan? minimumInterval = null,
        Func<double>? fallback = null) =>
        new(kernel.Read, minimumInterval ?? TimeSpan.Zero, fallback, clock.Now);

    [Fact]
    public void TheFirstReadingCoversTheTimeSinceTheSamplerWasCreated()
    {
        var kernel = new FakeKernel();
        var clock = new FakeClock();
        var sampler = Sampler(kernel, clock);

        kernel.Run(busy: 750, idle: 250);
        clock.Advance(TimeSpan.FromSeconds(1));

        sampler.Sample().Should().BeApproximately(75, 0.001,
            "the baseline was taken at creation, so the first reading is a real measurement, not a zero");
    }

    [Fact]
    public void TwoSamplersEachMeasureTheirOwnInterval()
    {
        var kernel = new FakeKernel();
        var clock = new FakeClock();
        var dashboard = Sampler(kernel, clock);
        var overlay = Sampler(kernel, clock);

        // An idle first second, which the overlay reads the moment it ends.
        kernel.Run(busy: 0, idle: 1000);
        clock.Advance(TimeSpan.FromSeconds(1));
        overlay.Sample().Should().Be(0, "the machine did nothing in the overlay's interval");

        // A flat-out second after it.
        kernel.Run(busy: 1000, idle: 0);
        clock.Advance(TimeSpan.FromSeconds(1));

        dashboard.Sample().Should().BeApproximately(50, 0.001,
            "the dashboard's interval is both seconds; with a shared baseline the overlay's read would have " +
            "cut it to the busy one and reported 100");
        overlay.Sample().Should().BeApproximately(100, 0.001, "the overlay's interval is the busy second alone");
    }

    [Fact]
    public void CountersThatHaveNotMovedReturnTheLastReadingRatherThanZero()
    {
        var kernel = new FakeKernel();
        var clock = new FakeClock();
        var sampler = Sampler(kernel, clock);

        kernel.Run(busy: 400, idle: 600);
        clock.Advance(TimeSpan.FromSeconds(1));
        sampler.Sample().Should().BeApproximately(40, 0.001);

        // Asked again before the kernel's counters have ticked: no interval, so no measurement.
        sampler.Sample().Should().BeApproximately(40, 0.001, "an unmeasured interval is not an idle machine");

        // The baseline was kept, so the next reading covers everything since the last real one.
        kernel.Run(busy: 1000, idle: 0);
        clock.Advance(TimeSpan.FromSeconds(1));
        sampler.Sample().Should().BeApproximately(100, 0.001);
    }

    [Fact]
    public void WithinTheMinimumIntervalTheLastReadingStandsAndTheKernelIsNotAsked()
    {
        var kernel = new FakeKernel();
        var clock = new FakeClock();
        var sampler = Sampler(kernel, clock, TimeSpan.FromMilliseconds(250));

        kernel.Run(busy: 300, idle: 700);
        clock.Advance(TimeSpan.FromMilliseconds(300));
        sampler.Sample().Should().BeApproximately(30, 0.001);
        var readsAfterFirst = kernel.Reads;

        kernel.Run(busy: 1000, idle: 0);
        clock.Advance(TimeSpan.FromMilliseconds(100));
        sampler.Sample().Should().BeApproximately(30, 0.001, "100 ms is inside the 250 ms window");
        kernel.Reads.Should().Be(readsAfterFirst, "a reading inside the window is the last one handed back");

        clock.Advance(TimeSpan.FromMilliseconds(200));
        sampler.Sample().Should().BeApproximately(100, 0.001, "300 ms after the last reading, a fresh one is taken");
    }

    [Fact]
    public void WhenTheKernelWillNotReportTheFallbackDecides()
    {
        var kernel = new FakeKernel { Reporting = false };
        var clock = new FakeClock();

        Sampler(kernel, clock, fallback: () => 42).Sample().Should().Be(42);
        Sampler(kernel, clock, fallback: () => 150).Sample().Should().Be(100, "a reading is clamped to 0-100");
        Sampler(kernel, clock, fallback: () => -5).Sample().Should().Be(0, "a reading is clamped to 0-100");
        Sampler(kernel, clock).Sample().Should().Be(0, "no counters and no fallback leaves nothing to report");
    }

    [Fact]
    public async Task ManyThreadsReadingOneSamplerStayInRangeAndNeverThrow()
    {
        var kernel = new FakeKernel();
        var clock = new FakeClock();
        var sampler = Sampler(kernel, clock);
        var readings = new System.Collections.Concurrent.ConcurrentBag<double>();

        // The machine keeps running while forty readers hammer the one sampler. Without the lock, a reader could
        // take the new idle count with the old kernel count and compute a percentage from two different moments.
        var readers = Enumerable.Range(0, 40).Select(worker => Task.Run(() =>
        {
            for (var i = 0; i < 250; i++)
            {
                kernel.Run(busy: (worker % 7) + 1, idle: (i % 5) + 1);
                clock.Advance(TimeSpan.FromMilliseconds(1));
                readings.Add(sampler.Sample());
            }
        }));

        var act = async () => await Task.WhenAll(readers);

        await act.Should().NotThrowAsync();
        readings.Should().HaveCount(40 * 250);
        readings.Should().AllSatisfy(usage => usage.Should().BeInRange(0, 100));
    }
}
