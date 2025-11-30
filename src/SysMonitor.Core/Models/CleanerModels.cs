namespace SysMonitor.Core.Models;

/// <summary>
/// Result of a cleaning operation.
/// </summary>
public class CleanerResult
{
    public bool Success { get; set; }
    public string Category { get; set; } = string.Empty;
    public long BytesCleaned { get; set; }
    public int FilesDeleted { get; set; }
    public int FoldersDeleted { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;

    public double MBCleaned => BytesCleaned / (1024.0 * 1024);
    public double GBCleaned => BytesCleaned / (1024.0 * 1024 * 1024);
}

/// <summary>
/// Scan result for cleanable items.
/// </summary>
public class CleanerScanResult
{
    public CleanerCategory Category { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int FileCount { get; set; }
    public bool IsSelected { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public CleanerRiskLevel RiskLevel { get; set; }

    public double SizeMB => SizeBytes / (1024.0 * 1024);

    /// <summary>
    /// Formatted size string that avoids scientific notation for small values.
    /// </summary>
    public string FormattedSize
    {
        get
        {
            if (SizeBytes >= 1_073_741_824)
                return $"{SizeBytes / 1_073_741_824.0:F2} GB";
            if (SizeBytes >= 1_048_576)
                return $"{SizeBytes / 1_048_576.0:F2} MB";
            if (SizeBytes >= 1024)
                return $"{SizeBytes / 1024.0:F2} KB";
            return $"{SizeBytes} B";
        }
    }
}

public enum CleanerCategory
{
    WindowsTemp,
    UserTemp,
    BrowserCache,
    BrowserCookies,
    BrowserHistory,
    RecycleBin,
    Thumbnails,
    WindowsUpdateCache,
    Prefetch,
    MemoryDumps,
    ErrorReports,
    LogFiles,
    Registry
}

public enum CleanerRiskLevel
{
    Safe,
    Low,
    Medium,
    High
}

/// <summary>
/// Registry issue found during scan.
/// </summary>
public class RegistryIssue
{
    public string Key { get; set; } = string.Empty;
    public string ValueName { get; set; } = string.Empty;
    public string IssueType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RegistryIssueCategory Category { get; set; }
    public CleanerRiskLevel RiskLevel { get; set; }
    public bool IsSelected { get; set; } = true;
    public bool IsFixed { get; set; }
}

public enum RegistryIssueCategory
{
    InvalidFileReference,
    OrphanedSoftware,
    InvalidShellExtension,
    InvalidStartupEntry,
    ObsoleteMUICache,
    InvalidTypeLib,
    InvalidCOM,
    InvalidFirewallRule,
    Other
}
