using Microsoft.EntityFrameworkCore;
using SysMonitor.Core.Data;
using SysMonitor.Core.Data.Entities;
using SysMonitor.Core.Services.Monitors;
using System.Collections.Concurrent;

namespace SysMonitor.Core.Services.History;

/// <summary>
/// Service for recording and retrieving historical metric data using SQLite.
/// Records metrics every 30 seconds and auto-purges data older than 30 days.
/// </summary>
public class HistoryService : IHistoryService
{
    private readonly ICpuMonitor _cpuMonitor;
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

    public HistoryService(
        ICpuMonitor cpuMonitor,
        IMemoryMonitor memoryMonitor,
        ITemperatureMonitor temperatureMonitor,
        INetworkMonitor networkMonitor,
        IBatteryMonitor batteryMonitor,
        IDiskMonitor diskMonitor)
    {
        _cpuMonitor = cpuMonitor;
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
            catch
            {
                // Silently continue on collection errors
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
            var cpuUsage = await _cpuMonitor.GetUsagePercentAsync();
            metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.Cpu, Value = cpuUsage });
        }
        catch { }

        // Memory Usage
        try
        {
            var memInfo = await _memoryMonitor.GetMemoryInfoAsync();
            metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.Memory, Value = memInfo.UsagePercent });
        }
        catch { }

        // CPU Temperature
        try
        {
            var cpuTemp = await _temperatureMonitor.GetCpuTemperatureAsync();
            if (cpuTemp > 0)
            {
                metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.CpuTemp, Value = cpuTemp });
            }
        }
        catch { }

        // GPU Temperature
        try
        {
            var gpuTemp = await _temperatureMonitor.GetGpuTemperatureAsync();
            if (gpuTemp > 0)
            {
                metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.GpuTemp, Value = gpuTemp });
            }
        }
        catch { }

        // Network
        try
        {
            var netInfo = await _networkMonitor.GetNetworkInfoAsync();
            metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.NetworkUp, Value = netInfo.UploadSpeedBps });
            metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.NetworkDown, Value = netInfo.DownloadSpeedBps });
        }
        catch { }

        // Battery
        try
        {
            var batteryInfo = await _batteryMonitor.GetBatteryInfoAsync();
            if (batteryInfo != null && batteryInfo.IsPresent)
            {
                metrics.Add(new MetricSnapshot { Timestamp = timestamp, MetricType = MetricTypes.Battery, Value = batteryInfo.ChargePercent });
            }
        }
        catch { }

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
        catch { }

        // Queue for batch write
        foreach (var metric in metrics)
        {
            _pendingWrites.Enqueue(metric);
        }
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(WriteIntervalSeconds));

        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            await FlushPendingWritesAsync();
        }

        // Final flush on cancellation
        await FlushPendingWritesAsync();
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
        _cts?.Dispose();
        _writeLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
