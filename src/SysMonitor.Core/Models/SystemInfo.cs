namespace SysMonitor.Core.Models;

/// <summary>
/// Comprehensive system information snapshot.
/// </summary>
public class SystemInfo
{
    public CpuInfo Cpu { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public List<DiskInfo> Disks { get; set; } = new();
    public BatteryInfo? Battery { get; set; }
    public NetworkInfo Network { get; set; } = new();
    public OsInfo OperatingSystem { get; set; } = new();
    public int HealthScore { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

/// <summary>
/// CPU information and usage statistics.
/// </summary>
public class CpuInfo
{
    public string Name { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public int Cores { get; set; }
    public int LogicalProcessors { get; set; }
    public double MaxClockSpeedMHz { get; set; }
    public double CurrentClockSpeedMHz { get; set; }
    public double UsagePercent { get; set; }
    public double Temperature { get; set; }
    public List<double> CoreUsages { get; set; } = new();
    public string Architecture { get; set; } = string.Empty;
    public string CacheL2 { get; set; } = string.Empty;
    public string CacheL3 { get; set; } = string.Empty;
}

/// <summary>
/// Memory (RAM) information.
/// </summary>
public class MemoryInfo
{
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public long AvailableBytes { get; set; }
    public double UsagePercent { get; set; }
    public long PageFileTotal { get; set; }
    public long PageFileUsed { get; set; }
    public int Speed { get; set; }
    public string Type { get; set; } = string.Empty;

    public double TotalGB => TotalBytes / (1024.0 * 1024 * 1024);
    public double UsedGB => UsedBytes / (1024.0 * 1024 * 1024);
    public double AvailableGB => AvailableBytes / (1024.0 * 1024 * 1024);
}

/// <summary>
/// Disk/Drive information.
/// </summary>
public class DiskInfo
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DriveType { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public long FreeBytes { get; set; }
    public double UsagePercent { get; set; }
    public bool IsSSD { get; set; }
    public string HealthStatus { get; set; } = "Unknown";
    public double Temperature { get; set; }
    public double ReadSpeedMBps { get; set; }
    public double WriteSpeedMBps { get; set; }

    public double TotalGB => TotalBytes / (1024.0 * 1024 * 1024);
    public double UsedGB => UsedBytes / (1024.0 * 1024 * 1024);
    public double FreeGB => FreeBytes / (1024.0 * 1024 * 1024);
}

/// <summary>
/// Battery information for laptops/tablets.
/// </summary>
public class BatteryInfo
{
    public bool IsPresent { get; set; }
    public bool IsCharging { get; set; }
    public bool IsPluggedIn { get; set; }
    public int ChargePercent { get; set; }
    public TimeSpan EstimatedRuntime { get; set; }
    public string HealthStatus { get; set; } = "Unknown";
    public int CycleCount { get; set; }
    public double DesignCapacityWh { get; set; }
    public double FullChargeCapacityWh { get; set; }
    public double CurrentCapacityWh { get; set; }
    public double Voltage { get; set; }
    public double ChargeRate { get; set; }
}

/// <summary>
/// Network information.
/// </summary>
public class NetworkInfo
{
    public bool IsConnected { get; set; }
    public string ConnectionType { get; set; } = string.Empty;
    public string AdapterName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public double UploadSpeedBps { get; set; }
    public double DownloadSpeedBps { get; set; }
    public int SignalStrength { get; set; }
    public string SSID { get; set; } = string.Empty;
    public List<NetworkAdapter> Adapters { get; set; } = new();

    public double UploadSpeedMbps => UploadSpeedBps / (1024 * 1024);
    public double DownloadSpeedMbps => DownloadSpeedBps / (1024 * 1024);
}

/// <summary>
/// Network adapter details.
/// </summary>
public class NetworkAdapter
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string MacAddress { get; set; } = string.Empty;
    public long Speed { get; set; }
}

/// <summary>
/// Operating system information.
/// </summary>
public class OsInfo
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Build { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public DateTime InstallDate { get; set; }
    public TimeSpan Uptime { get; set; }
    public string ComputerName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
}
