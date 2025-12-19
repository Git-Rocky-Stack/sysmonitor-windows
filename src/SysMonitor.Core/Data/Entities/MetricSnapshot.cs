namespace SysMonitor.Core.Data.Entities;

/// <summary>
/// Represents a single metric reading stored for historical tracking.
/// </summary>
public class MetricSnapshot
{
    public long Id { get; set; }

    /// <summary>
    /// Timestamp when the metric was recorded.
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Type of metric: CPU, Memory, CpuTemp, GpuTemp, Disk, NetworkUp, NetworkDown
    /// </summary>
    public string MetricType { get; set; } = string.Empty;

    /// <summary>
    /// The metric value (percentage, temperature in Celsius, or bytes/sec for network).
    /// </summary>
    public double Value { get; set; }
}

/// <summary>
/// Defines the metric types tracked in history.
/// </summary>
public static class MetricTypes
{
    public const string Cpu = "CPU";
    public const string Memory = "Memory";
    public const string CpuTemp = "CpuTemp";
    public const string GpuTemp = "GpuTemp";
    public const string Disk = "Disk";
    public const string NetworkUp = "NetworkUp";
    public const string NetworkDown = "NetworkDown";
    public const string Battery = "Battery";
}
