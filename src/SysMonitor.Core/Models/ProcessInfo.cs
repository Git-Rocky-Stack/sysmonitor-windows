namespace SysMonitor.Core.Models;

/// <summary>
/// Information about a running process.
/// </summary>
public class ProcessInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public ProcessStatus Status { get; set; }
    public long MemoryBytes { get; set; }
    public long PrivateMemoryBytes { get; set; }
    public long WorkingSetBytes { get; set; }
    public double CpuUsagePercent { get; set; }
    public long DiskReadBytesPerSec { get; set; }
    public long DiskWriteBytesPerSec { get; set; }
    public DateTime StartTime { get; set; }
    public TimeSpan TotalProcessorTime { get; set; }
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public ProcessPriority Priority { get; set; }
    public bool IsSystemProcess { get; set; }
    public bool IsElevated { get; set; }

    public double MemoryMB => MemoryBytes / (1024.0 * 1024);
    public double WorkingSetMB => WorkingSetBytes / (1024.0 * 1024);
}

public enum ProcessStatus
{
    Running,
    Suspended,
    NotResponding,
    Unknown
}

public enum ProcessPriority
{
    Idle,
    BelowNormal,
    Normal,
    AboveNormal,
    High,
    RealTime
}

/// <summary>
/// Startup program item.
/// </summary>
public class StartupItem
{
    public string Name { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public StartupItemType Type { get; set; }
    public bool IsEnabled { get; set; }
    public StartupImpact Impact { get; set; }
    public DateTime? LastRun { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string RegistryKey { get; set; } = string.Empty;
}

public enum StartupItemType
{
    Registry,
    StartupFolder,
    ScheduledTask,
    Service
}

public enum StartupImpact
{
    None,
    Low,
    Medium,
    High,
    Unknown
}

/// <summary>
/// Windows service information.
/// </summary>
public class ServiceInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public ServiceState Status { get; set; }
    public ServiceStartMode StartMode { get; set; }
    public string Account { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public bool CanStop { get; set; }
    public bool CanPauseAndContinue { get; set; }
}

public enum ServiceState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Paused,
    Unknown
}

public enum ServiceStartMode
{
    Automatic,
    AutomaticDelayed,
    Manual,
    Disabled,
    Unknown
}

/// <summary>
/// Installed application information.
/// </summary>
public class InstalledApp
{
    public string Name { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public DateTime InstallDate { get; set; }
    public long SizeBytes { get; set; }
    public string InstallLocation { get; set; } = string.Empty;
    public string UninstallString { get; set; } = string.Empty;
    public bool IsSystemComponent { get; set; }

    public double SizeMB => SizeBytes / (1024.0 * 1024);
}
