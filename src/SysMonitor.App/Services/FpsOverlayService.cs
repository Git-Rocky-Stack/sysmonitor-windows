using SysMonitor.App.Views;
using SysMonitor.Core.Services.GameMode;
using SysMonitor.Core.Services.Monitors;
using Serilog;

namespace SysMonitor.App.Services;

/// <summary>
/// Service for managing the FPS overlay window.
/// </summary>
public class FpsOverlayService : IFpsOverlayService
{
    /// <summary>How long shutdown waits for the update loop to notice it has been cancelled.</summary>
    public static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(2);

    // Its own baseline: this reads on its own timer, and a shared one would let every other caller cut its
    // measurement interval short (see CpuSampler).
    private readonly CpuSampler _cpuSampler;
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly ITemperatureMonitor _temperatureMonitor;

    private FpsOverlayWindow? _overlayWindow;
    private CancellationTokenSource? _cts;
    private Task? _updateTask;
    private OverlayPosition _position = OverlayPosition.TopRight;
    private int _updateIntervalMs = 500;
    private bool _disposed;

    public bool IsVisible { get; private set; }

    public OverlayPosition Position
    {
        get => _position;
        set
        {
            _position = value;
            if (_overlayWindow != null)
            {
                _overlayWindow.Position = value;
            }
        }
    }

    public int UpdateIntervalMs
    {
        get => _updateIntervalMs;
        set => _updateIntervalMs = Math.Max(100, Math.Min(5000, value));
    }

    public event EventHandler<OverlayStats>? StatsUpdated;

    public FpsOverlayService(
        ICpuMonitor cpuMonitor,
        IMemoryMonitor memoryMonitor,
        ITemperatureMonitor temperatureMonitor)
    {
        _cpuSampler = cpuMonitor.CreateSampler();
        _memoryMonitor = memoryMonitor;
        _temperatureMonitor = temperatureMonitor;
    }

    public async Task ShowAsync()
    {
        if (IsVisible) return;

        // Ensure temperature monitor is initialized
        await _temperatureMonitor.InitializeAsync();

        // Create window on UI thread
        _overlayWindow = new FpsOverlayWindow
        {
            Position = _position
        };
        _overlayWindow.ShowOverlay();

        IsVisible = true;

        // Start stats update loop
        _cts = new CancellationTokenSource();
        _updateTask = UpdateLoopAsync(_cts.Token);
    }

    public async Task HideAsync()
    {
        if (!IsVisible) return;

        // Stop update loop
        _cts?.Cancel();
        if (_updateTask != null)
        {
            try
            {
                await _updateTask;
            }
            catch (OperationCanceledException)
            {
                // Best effort: cancellation is how this loop is asked to stop.
            }
        }

        _cts?.Dispose();
        _cts = null;
        _updateTask = null;

        // Close window
        _overlayWindow?.Close();
        _overlayWindow = null;

        IsVisible = false;
    }

    /// <summary>
    /// Closes the overlay and stops its loop. The DI container calls this when the host is disposed at
    /// shutdown, which is the only thing that ever closes the overlay if the user did not toggle it off -
    /// and a WinUI app with a window still open does not exit.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _cts?.Cancel();
            _updateTask?.Wait(ShutdownTimeout);
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "The overlay's update loop did not stop cleanly");
        }

        try
        {
            _cts?.Dispose();
            _overlayWindow?.Close();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Closing the overlay window failed");
        }

        _cts = null;
        _updateTask = null;
        _overlayWindow = null;
        IsVisible = false;

        GC.SuppressFinalize(this);
    }

    public async Task ToggleAsync()
    {
        if (IsVisible)
        {
            await HideAsync();
        }
        else
        {
            await ShowAsync();
        }
    }

    private async Task UpdateLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_updateIntervalMs));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var stats = await CollectStatsAsync();
                _overlayWindow?.UpdateStats(stats);
                StatsUpdated?.Invoke(this, stats);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "A frame-rate sample failed; the overlay keeps running");
            }

            // Outside the catch above on purpose: a tick that throws must still cost a full interval,
            // or the loop comes straight back round and polls the hardware flat out.
            try
            {
                await timer.WaitForNextTickAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<OverlayStats> CollectStatsAsync()
    {
        var stats = new OverlayStats();

        try
        {
            // CPU usage
            stats.CpuUsage = _cpuSampler.Sample();

            // Memory
            var memInfo = await _memoryMonitor.GetMemoryInfoAsync();
            if (memInfo != null)
            {
                stats.RamUsageGb = memInfo.UsedBytes / (1024.0 * 1024.0 * 1024.0);
                stats.RamTotalGb = memInfo.TotalBytes / (1024.0 * 1024.0 * 1024.0);
            }

            // Temperatures - returns Dictionary<string, double>
            var temps = await _temperatureMonitor.GetAllTemperaturesAsync();
            foreach (var temp in temps)
            {
                var name = temp.Key;
                var value = temp.Value;

                if (name.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                {
                    if (value > stats.CpuTemperature)
                        stats.CpuTemperature = value;
                }
                else if (name.Contains("GPU", StringComparison.OrdinalIgnoreCase))
                {
                    if (value > stats.GpuTemperature)
                        stats.GpuTemperature = value;
                }
            }

            // Get load sensors for GPU usage and FPS
            var loadSensors = await _temperatureMonitor.GetAllLoadSensorsAsync();
            foreach (var load in loadSensors)
            {
                var key = load.Key.ToUpperInvariant();

                // GPU Core Load
                if (key.Contains("GPU") && key.Contains("CORE") && !key.Contains("MEMORY"))
                {
                    stats.GpuUsage = Math.Max(stats.GpuUsage, load.Value);
                }
                // GPU general load
                else if (key.Contains("GPU") && key.Contains("LOAD"))
                {
                    if (stats.GpuUsage == 0)
                        stats.GpuUsage = load.Value;
                }
            }

            // Frame rate comes from a Factor sensor, which is not in this list - looking for it here is why
            // the overlay's FPS readout was blank on every machine, including ones that could report it.
            stats.FrameRate = await _temperatureMonitor.GetFrameRateAsync();

            // Power readings
            var powerReadings = await _temperatureMonitor.GetAllPowerReadingsAsync();
            foreach (var power in powerReadings)
            {
                var key = power.Key.ToUpperInvariant();
                if (key.Contains("CPU"))
                {
                    // Prefer Package power, but take any CPU power
                    if (key.Contains("PACKAGE") || stats.CpuPowerWatts == 0)
                    {
                        stats.CpuPowerWatts = Math.Max(stats.CpuPowerWatts, power.Value);
                    }
                }
                else if (key.Contains("GPU") || key.Contains("GRAPHICS"))
                {
                    stats.GpuPowerWatts = Math.Max(stats.GpuPowerWatts, power.Value);
                }
            }

            // If no specific readings, try to get any power readings
            if (stats.CpuPowerWatts == 0 && stats.GpuPowerWatts == 0 && powerReadings.Count > 0)
            {
                var sortedPower = powerReadings.OrderByDescending(p => p.Value).ToList();
                if (sortedPower.Count > 0) stats.CpuPowerWatts = sortedPower[0].Value;
                if (sortedPower.Count > 1) stats.GpuPowerWatts = sortedPower[1].Value;
            }

            stats.SystemPowerWatts = stats.CpuPowerWatts + stats.GpuPowerWatts;

            // Fan speeds
            stats.FanSpeeds = await _temperatureMonitor.GetAllFanSpeedsAsync();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Frame-rate statistics are incomplete");
        }

        return stats;
    }
}
