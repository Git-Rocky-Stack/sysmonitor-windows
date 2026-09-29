using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using SysMonitor.Core.Data;
using SysMonitor.Core.Data.Entities;
using SysMonitor.Core.Services.Monitors;
using System.Collections.Concurrent;

namespace SysMonitor.Core.Services.History;

/// <summary>
/// Service for recording and retrieving historical metric data using SQLite.
/// Records metrics every 30 seconds. Data older than 30 days is removed when the service starts, so a run
/// that never restarts keeps everything it has recorded.
/// </summary>
public class HistoryService : IHistoryService
{
    private readonly ILogger _logger;

    // Its own baseline: this reads on its own timer, and a shared one would let every other caller cut its
    // measurement interval short (see CpuSampler).
    private readonly CpuSampler _cpuSampler;
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly ITemperatureMonitor _temperatureMonitor;
    private readonly INetworkMonitor _networkMonitor;
    private readonly IBatteryMonitor _batteryMonitor;
    private readonly IDiskMonitor _diskMonitor;

    private readonly ConcurrentQueue<MetricSnapshot> _pendingWrites = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _recordingTask;
    private Task? _writeTask;

    private const int RecordingIntervalSeconds = 30;
    private const int WriteIntervalSeconds = 60;
    private const int RetentionDays = 30;

    /// <summary>How long Dispose waits for the loops to flush what they still hold before giving up.</summary>
    private static readonly TimeSpan ShutdownFlushTimeout = TimeSpan.FromSeconds(5);

    public HistoryService(
        ICpuMonitor cpuMonitor,
        IMemoryMonitor memoryMonitor,
        ITemperatureMonitor temperatureMonitor,
        INetworkMonitor networkMonitor,
        IBatteryMonitor batteryMonitor,
        IDiskMonitor diskMonitor,
        ILogger<HistoryService>? logger = null)
    {
        _logger = logger ?? NullLogger<HistoryService>.Instance;
        _cpuSampler = cpuMonitor.CreateSampler();
        _memoryMonitor = memoryMonitor;
        _temperatureMonitor = temperatureMonitor;
        _networkMonitor = networkMonitor;
        _batteryMonitor = batteryMonitor;
        _diskMonitor = diskMonitor;
    }

    public async Task InitializeAsync()
    {
        // Ensure database is created
        using var context = new HistoryDbContext();
        await context.Database.EnsureCreatedAsync();

        // Purge old data on startup
        await PurgeOldDataAsync(DateTime.UtcNow.AddDays(-RetentionDays));

        // Start background tasks
        _cts = new CancellationTokenSource();
        _recordingTask = RecordingLoopAsync(_cts.Token);
        _writeTask = WriteLoopAsync(_cts.Token);
    }

    public void StopRecording()
    {
        _cts?.Cancel();
    }

    private async Task RecordingLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(RecordingIntervalSeconds));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            try
            {
                await CollectAndQueueMetricsAsync();
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Collecting a history snapshot failed; the next tick tries again");
            }
        }
    }

    private async Task CollectAndQueueMetricsAsync()
    {
        var timestamp = DateTime.UtcNow;
        var metrics = new List<MetricSnapshot>();

        // CPU Usage
        try
        {
            var cpuUsage = _cpuSampler.Sample();
            metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.Cpu, Value = cpuUsage });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CollectAndQueueMetricsAsync failed");
        }

        // Memory Usage
        try
        {
            var memInfo = await _memoryMonitor.GetMemoryInfoAsync();
            metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.Memory, Value = memInfo.UsagePercent });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CollectAndQueueMetricsAsync failed");
        }

        // CPU Temperature
        try
        {
            var cpuTemp = await _temperatureMonitor.GetCpuTemperatureAsync();
            if (cpuTemp > 0)
            {
                metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.CpuTemp, Value = cpuTemp });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CollectAndQueueMetricsAsync failed");
        }

        // GPU Temperature
        try
        {
            var gpuTemp = await _temperatureMonitor.GetGpuTemperatureAsync();
            if (gpuTemp > 0)
            {
                metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.GpuTemp, Value = gpuTemp });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CollectAndQueueMetricsAsync failed");
        }

        // Network
        try
        {
            var netInfo = await _networkMonitor.GetNetworkInfoAsync();
            metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.NetworkUp, Value = netInfo.UploadSpeedBps });
            metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.NetworkDown, Value = netInfo.DownloadSpeedBps });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CollectAndQueueMetricsAsync failed");
        }

        // Battery
        try
        {
            var batteryInfo = await _batteryMonitor.GetBatteryInfoAsync();
            if (batteryInfo != null && batteryInfo.IsPresent)
            {
                metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.Battery, Value = batteryInfo.ChargePercent });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CollectAndQueueMetricsAsync failed");
        }

        // Disk (primary drive)
        try
        {
            var diskInfo = await _diskMonitor.GetAllDisksAsync();
            var primaryDisk = diskInfo.FirstOrDefault();
            if (primaryDisk != null)
            {
                metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.Disk, Value = primaryDisk.UsagePercent });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CollectAndQueueMetricsAsync failed");
        }

        // Queue for batch write
        foreach (var metric in metrics)
        {
            _pendingWrites.Enqueue(metric);
        }
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(WriteIntervalSeconds));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await FlushPendingWritesAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Best effort: stopping is how this loop ends, and the flush below is the point of
            // stopping cleanly.
        }
        finally
        {
            // Up to a full write interval of snapshots is still queued here. Cancellation throws out of
            // the condition above, so this only runs at all because it is in a finally.
            await FlushPendingWritesAsync();
        }
    }

    private async Task FlushPendingWritesAsync()
    {
        if (_pendingWrites.IsEmpty) return;

        await _writeLock.WaitAsync();
        try
        {
            var batch = new List<MetricSnapshot>();
            while (_pendingWrites.TryDequeue(out var snapshot))
            {
                batch.Add(snapshot);
            }

            if (batch.Count > 0)
            {
                using var context = new HistoryDbContext();
                context.MetricSnapshots.AddRange(batch);
                await context.SaveChangesAsync();
            }
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task RecordMetricAsync(string metricType, double value)
    {
        _pendingWrites.Enqueue(new MetricSnapshot
        {
            Timestamp = DateTime.UtcNow,
            MetricType = metricType,
            Value = value
        });
        await Task.CompletedTask;
    }

    public async Task RecordMetricsAsync(IEnumerable<(string MetricType, double Value)> metrics)
    {
        var timestamp = DateTime.UtcNow;
        foreach (var (metricType, value) in metrics)
        {
            _pendingWrites.Enqueue(new MetricSnapshot
            {
                Timestamp = timestamp,
                MetricType = metricType,
                Value = value
            });
        }
        await Task.CompletedTask;
    }

    public async Task<List<MetricSnapshot>> GetMetricHistoryAsync(string metricType, DateTime startTime, DateTime? endTime = null)
    {
        var end = endTime ?? DateTime.UtcNow;

        using var context = new HistoryDbContext();
        return await context.MetricSnapshots
            .Where(m => m.MetricType == metricType && m.Timestamp >= startTime && m.Timestamp <= end)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();
    }

    public async Task<List<MetricSnapshot>> GetAggregatedHistoryAsync(string metricType, DateTime startTime, DateTime? endTime = null, int maxPoints = 200)
    {
        var end = endTime ?? DateTime.UtcNow;

        using var context = new HistoryDbContext();
        var allData = await context.MetricSnapshots
            .Where(m => m.MetricType == metricType && m.Timestamp >= startTime && m.Timestamp <= end)
            .OrderBy(m => m.Timestamp)
            .ToListAsync();

        if (allData.Count <= maxPoints)
        {
            return allData;
        }

        // Downsample by taking every Nth point
        var step = (double)allData.Count / maxPoints;
        var result = new List<MetricSnapshot>();

        for (int i = 0; i < maxPoints; i++)
        {
            var index = (int)(i * step);
            if (index < allData.Count)
            {
                result.Add(allData[index]);
            }
        }

        return result;
    }

    public async Task PurgeOldDataAsync(DateTime olderThan)
    {
        using var context = new HistoryDbContext();
        var oldRecords = await context.MetricSnapshots
            .Where(m => m.Timestamp < olderThan)
            .ToListAsync();

        if (oldRecords.Count > 0)
        {
            context.MetricSnapshots.RemoveRange(oldRecords);
            await context.SaveChangesAsync();
        }
    }

    public async Task<int> GetMetricCountAsync()
    {
        using var context = new HistoryDbContext();
        return await context.MetricSnapshots.CountAsync();
    }

    public void Dispose()
    {
        StopRecording();

        // The write loop still has a final flush to do, and it needs _writeLock to do it. Disposing the
        // lock out from under it turns the last snapshots into an ObjectDisposedException nobody sees.
        // The wait is bounded so a stuck write cannot hold the app open.
        try
        {
            var running = new[] { _recordingTask, _writeTask }.Where(task => task is not null).ToArray()!;
            if (running.Length > 0)
                Task.WaitAll(running!, ShutdownFlushTimeout);
        }
        catch (AggregateException)
        {
            // Best effort: the loops are stopping and their own failures are logged where they happen.
        }

        _cts?.Dispose();
        _writeLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
