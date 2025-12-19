using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SysMonitor.Core.Services.Monitoring;

/// <summary>
/// Implementation of performance monitoring for the application.
/// </summary>
public class PerformanceMonitor : IPerformanceMonitor
{
    private readonly ILogger<PerformanceMonitor> _logger;
    private readonly ConcurrentDictionary<string, OperationMetrics> _operationMetrics = new();
    private readonly ConcurrentDictionary<string, List<double>> _counterValues = new();
    private readonly int _maxSamplesPerMetric = 1000;

    public PerformanceMonitor(ILogger<PerformanceMonitor> logger)
    {
        _logger = logger;
    }

    public void RecordOperationTime(string operationName, TimeSpan duration)
    {
        var metrics = _operationMetrics.GetOrAdd(operationName, _ => new OperationMetrics());
        metrics.AddSample(duration.TotalMilliseconds);

        // Log slow operations
        if (duration.TotalMilliseconds > 100)
        {
            _logger.LogWarning("Slow operation detected: {Operation} took {Duration}ms",
                operationName, duration.TotalMilliseconds);
        }
    }

    public void RecordMemoryUsage(string context, long bytesUsed)
    {
        RecordCounter($"Memory_{context}", bytesUsed / (1024.0 * 1024.0)); // Convert to MB
    }

    public void RecordCounter(string counterName, double value)
    {
        var values = _counterValues.GetOrAdd(counterName, _ => new List<double>());

        lock (values)
        {
            values.Add(value);

            // Limit samples to prevent unbounded growth
            if (values.Count > _maxSamplesPerMetric)
            {
                values.RemoveRange(0, values.Count - _maxSamplesPerMetric);
            }
        }
    }

    public PerformanceStats? GetOperationStats(string operationName)
    {
        if (!_operationMetrics.TryGetValue(operationName, out var metrics))
            return null;

        return metrics.GetStats();
    }

    public Dictionary<string, PerformanceStats> GetAllStats()
    {
        var result = new Dictionary<string, PerformanceStats>();

        foreach (var kvp in _operationMetrics)
        {
            var stats = kvp.Value.GetStats();
            if (stats != null)
            {
                result[kvp.Key] = stats;
            }
        }

        return result;
    }

    public void Clear()
    {
        _operationMetrics.Clear();
        _counterValues.Clear();
    }

    public IDisposable TrackOperation(string operationName)
    {
        return new OperationTracker(this, operationName);
    }

    /// <summary>
    /// Internal class for tracking operation metrics.
    /// </summary>
    private class OperationMetrics
    {
        private readonly List<double> _samples = new();
        private readonly object _lock = new();
        private DateTime _lastRecorded = DateTime.UtcNow;

        public void AddSample(double milliseconds)
        {
            lock (_lock)
            {
                _samples.Add(milliseconds);
                _lastRecorded = DateTime.UtcNow;

                // Keep only last 1000 samples
                if (_samples.Count > 1000)
                {
                    _samples.RemoveAt(0);
                }
            }
        }

        public PerformanceStats? GetStats()
        {
            lock (_lock)
            {
                if (_samples.Count == 0)
                    return null;

                var sorted = _samples.OrderBy(x => x).ToList();
                var p95Index = (int)(sorted.Count * 0.95);
                var p99Index = (int)(sorted.Count * 0.99);

                return new PerformanceStats
                {
                    Count = _samples.Count,
                    AverageMs = _samples.Average(),
                    MinMs = _samples.Min(),
                    MaxMs = _samples.Max(),
                    LastMs = _samples.Last(),
                    LastRecorded = _lastRecorded,
                    P95Ms = sorted[Math.Min(p95Index, sorted.Count - 1)],
                    P99Ms = sorted[Math.Min(p99Index, sorted.Count - 1)]
                };
            }
        }
    }

    /// <summary>
    /// Disposable tracker for measuring operation duration.
    /// </summary>
    private class OperationTracker : IDisposable
    {
        private readonly PerformanceMonitor _monitor;
        private readonly string _operationName;
        private readonly Stopwatch _stopwatch;

        public OperationTracker(PerformanceMonitor monitor, string operationName)
        {
            _monitor = monitor;
            _operationName = operationName;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _monitor.RecordOperationTime(_operationName, _stopwatch.Elapsed);
        }
    }
}