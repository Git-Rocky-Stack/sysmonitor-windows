namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Service for enumerating and managing installed programs
/// </summary>
public interface IInstalledProgramsService
{
    /// <summary>
    /// Get all installed programs from all sources (Registry, Store apps, etc.)
    /// </summary>
    Task<List<InstalledProgram>> GetInstalledProgramsAsync();

    /// <summary>
    /// Uninstall a program
    /// </summary>
    Task<UninstallResult> UninstallProgramAsync(InstalledProgram program);

    /// <summary>
    /// Open the program's install location in Explorer
    /// </summary>
    void OpenInstallLocation(InstalledProgram program);
}

/// <summary>
/// Represents an installed program
/// </summary>
public class InstalledProgram
{
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public DateTime? InstallDate { get; set; }
    public long EstimatedSizeBytes { get; set; }
    public string UninstallString { get; set; } = "";
    public string QuietUninstallString { get; set; } = "";
    public ProgramType Type { get; set; }
    public string PackageFullName { get; set; } = ""; // For Store apps
    public string RegistryKey { get; set; } = ""; // For Win32 apps
    public bool IsSystemApp { get; set; }
    public string Icon { get; set; } = "\uE74C"; // Default app icon

    /// <summary>
    /// Formatted size string
    /// </summary>
    public string FormattedSize
    {
        get
        {
            if (EstimatedSizeBytes <= 0) return "Unknown";
            if (EstimatedSizeBytes >= 1_073_741_824)
                return $"{EstimatedSizeBytes / 1_073_741_824.0:F2} GB";
            if (EstimatedSizeBytes >= 1_048_576)
                return $"{EstimatedSizeBytes / 1_048_576.0:F2} MB";
            if (EstimatedSizeBytes >= 1024)
                return $"{EstimatedSizeBytes / 1024.0:F2} KB";
            return $"{EstimatedSizeBytes} B";
        }
    }

    /// <summary>
    /// Formatted install date
    /// </summary>
    public string FormattedInstallDate => InstallDate?.ToString("MMM dd, yyyy") ?? "Unknown";

    /// <summary>
    /// Type display string
    /// </summary>
    public string TypeDisplay => Type switch
    {
        ProgramType.Win32 => "Desktop App",
        ProgramType.StoreApp => "Store App",
        ProgramType.SystemApp => "System App",
        ProgramType.Framework => "Framework",
        _ => "Unknown"
    };

    /// <summary>
    /// Type color for UI
    /// </summary>
    public string TypeColor => Type switch
    {
        ProgramType.Win32 => "#4CAF50",
        ProgramType.StoreApp => "#2196F3",
        ProgramType.SystemApp => "#FF9800",
        ProgramType.Framework => "#9C27B0",
        _ => "#808080"
    };

    /// <summary>
    /// Whether this program can be uninstalled
    /// </summary>
    public bool CanUninstall => !string.IsNullOrEmpty(UninstallString) ||
                                 !string.IsNullOrEmpty(PackageFullName);
}

public enum ProgramType
{
    Win32,      // Traditional desktop application
    StoreApp,   // Microsoft Store / UWP app
    SystemApp,  // Pre-installed system app (Xbox, etc.)
    Framework   // Runtime/Framework (.NET, VC++, etc.)
}

public class UninstallResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = "";
    public int ExitCode { get; set; }
}
