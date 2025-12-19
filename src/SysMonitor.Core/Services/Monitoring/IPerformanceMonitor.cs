namespace SysMonitor.Core.Services.Monitoring;

/// <summary>
/// Interface for monitoring application performance metrics.
/// </summary>
public interface IPerformanceMonitor
{
    /// <summary>
    /// Records the time taken for an operation.
    /// </summary>
    void RecordOperationTime(string operationName, TimeSpan duration);

    /// <summary>
    /// Records memory usage at a specific point.
    /// </summary>
    void RecordMemoryUsage(string context, long bytesUsed);

    /// <summary>
    /// Records a performance counter value.
    /// </summary>
    void RecordCounter(string counterName, double value);

    /// <summary>
    /// Gets performance statistics for a specific operation.
    /// </summary>
    PerformanceStats? GetOperationStats(string operationName);

    /// <summary>
    /// Gets all recorded performance statistics.
    /// </summary>
    Dictionary<string, PerformanceStats> GetAllStats();

    /// <summary>
    /// Clears all recorded statistics.
    /// </summary>
    void Clear();

    /// <summary>
    /// Starts tracking an operation. Returns a disposable that stops tracking when disposed.
    /// </summary>
    IDisposable TrackOperation(string operationName);
}

/// <summary>
/// Performance statistics for an operation.
/// </summary>
public record PerformanceStats
{
    public int Count { get; init; }
    public double AverageMs { get; init; }
    public double MinMs { get; init; }
    public double MaxMs { get; init; }
    public double LastMs { get; init; }
    public DateTime LastRecorded { get; init; }
    public double P95Ms { get; init; }
    public double P99Ms { get; init; }
}