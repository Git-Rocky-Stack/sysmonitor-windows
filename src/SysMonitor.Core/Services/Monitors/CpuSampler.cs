using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SysMonitor.Core.Services.Monitors;

/// <summary>
/// Total CPU usage, measured over one caller's own interval.
/// <para>
/// Usage is the share of the time between two readings that the processors spent busy, so a reading is only
/// as good as the interval behind it. <see cref="CpuMonitor"/> used to keep a single baseline for the whole
/// application. The dashboard, the FPS overlay, the history recorder and the tray tooltip all read through it
/// on their own timers, and every call reset the baseline for everyone else: the overlay's 500 ms loop left
/// the dashboard's five-second refresh measuring whatever sliver of time had passed since the overlay last
/// asked, and two calls a few milliseconds apart measured nothing at all and reported it as zero. The four
/// baseline fields were read and written with no lock, from whichever thread got there first.
/// </para>
/// <para>
/// A sampler owns its baseline. Each long-lived consumer holds its own (<see cref="ICpuMonitor.CreateSampler"/>),
/// so what it reads covers the time since its own previous reading and nobody else's.
/// </para>
/// </summary>
public sealed class CpuSampler
{
    private readonly Func<SystemTimes?> _readTimes;
    private readonly Func<double>? _fallback;
    private readonly TimeSpan _minimumInterval;
    private readonly Func<long> _clock;
    private readonly object _gate = new();

    private SystemTimes? _previous;
    private long _previousAt;
    private double _lastUsage;

    /// <summary>A sampler that takes a fresh reading every time it is asked.</summary>
    public CpuSampler()
        : this(TimeSpan.Zero)
    {
    }

    /// <summary>
    /// A sampler that hands back its last reading when asked again within <paramref name="minimumInterval"/>.
    /// That is only worth having where several callers still share one sampler: it stops the second of two
    /// near-simultaneous calls from measuring an interval too short to mean anything.
    /// </summary>
    /// <param name="minimumInterval">How long a reading stands before another is taken.</param>
    /// <param name="fallback">Consulted when the kernel will not report its times.</param>
    public CpuSampler(TimeSpan minimumInterval, Func<double>? fallback = null)
        : this(ReadSystemTimes, minimumInterval, fallback, Stopwatch.GetTimestamp)
    {
    }

    /// <summary>The seam the tests drive: the kernel's counters and the clock, supplied by the caller.</summary>
    internal CpuSampler(Func<SystemTimes?> readTimes, TimeSpan minimumInterval, Func<double>? fallback, Func<long> clock)
    {
        _readTimes = readTimes;
        _minimumInterval = minimumInterval;
        _fallback = fallback;
        _clock = clock;

        // The baseline is taken now, so the first reading measures the time since this sampler was created. A
        // sampler that only started the clock on its first call would report 0% for it - and the history
        // recorder would write that zero into the chart at every start-up.
        _previous = _readTimes();
        _previousAt = _clock();
    }

    /// <summary>
    /// Percent of the time since this sampler's previous reading - or since it was created, for the first -
    /// that the processors were busy, 0 to 100.
    /// </summary>
    public double Sample()
    {
        lock (_gate)
        {
            var now = _clock();
            if (_previous is not null && _minimumInterval > TimeSpan.Zero &&
                Stopwatch.GetElapsedTime(_previousAt, now) < _minimumInterval)
            {
                return _lastUsage;
            }

            var times = _readTimes();
            if (times is null)
            {
                _lastUsage = Clamp(_fallback?.Invoke() ?? 0);
                return _lastUsage;
            }

            // Only when the kernel would not report its times at creation: this reading becomes the baseline.
            if (_previous is not { } previous)
            {
                _previous = times;
                _previousAt = now;
                return 0;
            }

            var idle = times.Value.Idle - previous.Idle;
            var total = (times.Value.Kernel - previous.Kernel) + (times.Value.User - previous.User);

            // The kernel's counters advance in coarse steps, so two readings close enough together see the same
            // values. That is not an idle machine, it is no measurement: hand back the last real reading and keep
            // the old baseline, so the next one covers the whole interval instead of the sliver after this call.
            if (total <= 0)
                return _lastUsage;

            _previous = times;
            _previousAt = now;

            // Kernel time includes idle time, so busy is everything the kernel and user modes spent not idle.
            _lastUsage = Clamp((total - idle) * 100.0 / total);
            return _lastUsage;
        }
    }

    private static double Clamp(double usage) => Math.Max(0, Math.Min(100, usage));

    /// <summary>The kernel's cumulative idle, kernel and user times, or null when it will not report them.</summary>
    private static SystemTimes? ReadSystemTimes()
    {
        try
        {
            return GetSystemTimes(out var idle, out var kernel, out var user)
                ? new SystemTimes(idle.ToLong(), kernel.ToLong(), user.ToLong())
                : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Not Windows, or a kernel32 without the export: nothing to read, so the fallback decides.
            return null;
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;
        public readonly long ToLong() => ((long)dwHighDateTime << 32) | dwLowDateTime;
    }
}

/// <summary>Cumulative processor times as <c>GetSystemTimes</c> reports them, in 100 ns ticks.</summary>
internal readonly record struct SystemTimes(long Idle, long Kernel, long User);
