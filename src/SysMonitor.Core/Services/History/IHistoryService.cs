using SysMonitor.Core.Data.Entities;

namespace SysMonitor.Core.Services.History;

/// <summary>
/// Service interface for recording and retrieving historical metric data.
/// </summary>
public interface IHistoryService : IDisposable
{
    /// <summary>
    /// Records a single metric snapshot.
    /// </summary>
    Task RecordMetricAsync(string metricType, double value);

    /// <summary>
    /// Records multiple metric snapshots in a batch.
    /// </summary>
    Task RecordMetricsAsync(IEnumerable<(string MetricType, double Value)> metrics);

    /// <summary>
    /// Gets metric history for a specific type within a time range.
    /// </summary>
    /// <param name="metricType">The type of metric to retrieve.</param>
    /// <param name="startTime">Start of the time range.</param>
    /// <param name="endTime">End of the time range (defaults to now).</param>
    /// <returns>List of metric snapshots ordered by timestamp.</returns>
    Task<List<MetricSnapshot>> GetMetricHistoryAsync(string metricType, DateTime startTime, DateTime? endTime = null);

    /// <summary>
    /// Gets aggregated metric data for charting (downsampled for performance).
    /// </summary>
    /// <param name="metricType">The type of metric to retrieve.</param>
    /// <param name="startTime">Start of the time range.</param>
    /// <param name="endTime">End of the time range.</param>
    /// <param name="maxPoints">Maximum number of data points to return.</param>
    /// <returns>Downsampled list of metric snapshots.</returns>
    Task<List<MetricSnapshot>> GetAggregatedHistoryAsync(string metricType, DateTime startTime, DateTime? endTime = null, int maxPoints = 200);

    /// <summary>
    /// Purges metric data older than the specified date.
    /// </summary>
    Task PurgeOldDataAsync(DateTime olderThan);

    /// <summary>
    /// Gets the total count of stored metrics.
    /// </summary>
    Task<int> GetMetricCountAsync();

    /// <summary>
    /// Initializes the database and starts background recording.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Stops background recording.
    /// </summary>
    void StopRecording();
}
