namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Service for scanning and managing system drivers
/// </summary>
public interface IDriverUpdater
{
    /// <summary>
    /// Scans all installed drivers on the system
    /// </summary>
    Task<List<DriverInfo>> ScanDriversAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks for available driver updates via Windows Update
    /// </summary>
    Task<List<DriverUpdate>> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets drivers that have problems or warnings
    /// </summary>
    Task<List<DriverInfo>> GetProblemDriversAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens Device Manager for manual driver management
    /// </summary>
    void OpenDeviceManager();

    /// <summary>
    /// Opens Windows Update settings
    /// </summary>
    void OpenWindowsUpdate();

    /// <summary>
    /// Exports driver report to a file
    /// </summary>
    Task<string> ExportDriverReportAsync(string filePath, CancellationToken cancellationToken = default);
}

// Driver Models

public record DriverInfo
{
    public string DeviceName { get; init; } = "";
    public string DeviceId { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string DriverVersion { get; init; } = "";
    public DateTime? DriverDate { get; init; }
    public string DriverProvider { get; init; } = "";
    public string DeviceClass { get; init; } = "";
    public string DeviceClassIcon { get; init; } = "\uE964"; // Default device icon
    public string InfName { get; init; } = "";
    public bool IsSigned { get; init; }
    public string Status { get; init; } = "OK";
    public string StatusColor { get; init; } = "#4CAF50";
    public bool HasProblem { get; init; }
    public string ProblemDescription { get; init; } = "";
    public int DaysSinceUpdate { get; init; }
    public bool IsOutdated { get; init; } // Older than 2 years
    public bool IsCritical { get; init; } // GPU, Network, Chipset, etc.
}

public record DriverUpdate
{
    public string DeviceName { get; init; } = "";
    public string CurrentVersion { get; init; } = "";
    public string NewVersion { get; init; } = "";
    public DateTime? ReleaseDate { get; init; }
    public string UpdateSource { get; init; } = "Windows Update";
    public long SizeBytes { get; init; }
    public string Description { get; init; } = "";
    public bool IsOptional { get; init; }
}

public record DriverScanResult
{
    public int TotalDrivers { get; init; }
    public int UpToDateDrivers { get; init; }
    public int OutdatedDrivers { get; init; }
    public int ProblemDrivers { get; init; }
    public int UnsignedDrivers { get; init; }
    public int AvailableUpdates { get; init; }
    public DateTime ScanTime { get; init; } = DateTime.Now;
}
