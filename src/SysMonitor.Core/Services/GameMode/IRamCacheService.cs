namespace SysMonitor.Core.Services.GameMode;

/// <summary>
/// Statistics about the RAM cache.
/// </summary>
public class RamCacheStats
{
    public bool IsEnabled { get; set; }
    public long AllocatedBytes { get; set; }
    public long UsedBytes { get; set; }
    public double UsagePercent => AllocatedBytes > 0 ? (UsedBytes * 100.0 / AllocatedBytes) : 0;
    public int FileCount { get; set; }
    public string CachePath { get; set; } = "";
}

/// <summary>
/// Service for managing a RAM-based temp cache for faster I/O.
/// </summary>
public interface IRamCacheService
{
    /// <summary>
    /// Whether the RAM cache is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Allocated cache size in bytes.
    /// </summary>
    long AllocatedSizeBytes { get; }

    /// <summary>
    /// Currently used cache size in bytes.
    /// </summary>
    long UsedSizeBytes { get; }

    /// <summary>
    /// Path to the cache directory.
    /// </summary>
    string CachePath { get; }

    /// <summary>
    /// Event raised when cache stats are updated.
    /// </summary>
    event EventHandler<RamCacheStats>? StatsUpdated;

    /// <summary>
    /// Enable the RAM cache with specified size.
    /// </summary>
    /// <param name="sizeMegabytes">Cache size in MB (512, 1024, 2048, 4096)</param>
    Task<bool> EnableAsync(long sizeMegabytes);

    /// <summary>
    /// Disable the RAM cache and restore original temp paths.
    /// </summary>
    Task DisableAsync();

    /// <summary>
    /// Get current cache statistics.
    /// </summary>
    Task<RamCacheStats> GetStatsAsync();

    /// <summary>
    /// Clear all files in the cache.
    /// </summary>
    Task ClearCacheAsync();
}
