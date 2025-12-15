namespace SysMonitor.Core.Services.Backup;

/// <summary>
/// Comprehensive backup service for Windows - File, Folder, System Image, and Incremental backups
/// </summary>
public interface IBackupService
{
    // Backup Operations
    Task<BackupResult> CreateBackupAsync(BackupJob job, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<BackupResult> CreateSystemImageAsync(string destinationPath, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<BackupResult> CreateRestorePointAsync(string description);

    // Restore Operations
    Task<BackupResult> RestoreBackupAsync(BackupArchive archive, string destinationPath, RestoreOptions options, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<List<RestorePointInfo>> GetRestorePointsAsync();

    // Backup Management
    Task<List<BackupArchive>> GetBackupHistoryAsync(string? backupLocation = null);
    Task<BackupResult> VerifyBackupAsync(BackupArchive archive, IProgress<BackupProgress>? progress = null);
    Task<BackupResult> DeleteBackupAsync(BackupArchive archive);

    // Scheduling
    Task<bool> ScheduleBackupAsync(BackupSchedule schedule);
    Task<bool> RemoveScheduledBackupAsync(string scheduleId);
    Task<List<BackupSchedule>> GetScheduledBackupsAsync();

    // Utilities
    Task<DriveSpaceInfo> GetDriveSpaceAsync(string drivePath);
    Task<List<DriveInfo>> GetAvailableDrivesAsync();
    Task<long> EstimateBackupSizeAsync(BackupJob job);
    bool IsBackupInProgress { get; }
}

// ==================== ENUMS ====================

public enum BackupType
{
    Full,           // Complete backup of all selected items
    Incremental,    // Only files changed since last backup
    Differential,   // All files changed since last full backup
    SystemImage,    // Full system/partition image
    Mirror          // Exact mirror (deletes files not in source)
}

public enum BackupDestinationType
{
    LocalDrive,
    ExternalUsb,
    NetworkShare,
    IsoFile
}

public enum BackupCompression
{
    None,
    Fast,       // Low compression, fast speed
    Normal,     // Balanced
    Maximum     // High compression, slower
}

public enum BackupFrequency
{
    Once,
    Daily,
    Weekly,
    Monthly,
    Custom
}

public enum BackupStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled,
    PartialSuccess,
    Verifying
}

// ==================== BACKUP JOB ====================

/// <summary>
/// Defines a backup job configuration
/// </summary>
public class BackupJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Backup";
    public BackupType Type { get; set; } = BackupType.Full;
    public BackupDestinationType DestinationType { get; set; } = BackupDestinationType.LocalDrive;

    // Source
    public List<string> SourcePaths { get; set; } = [];
    public List<string> ExcludePaths { get; set; } = [];
    public List<string> ExcludePatterns { get; set; } = ["*.tmp", "*.temp", "~*", "Thumbs.db", "desktop.ini"];
    public bool IncludeHiddenFiles { get; set; } = false;
    public bool IncludeSystemFiles { get; set; } = false;

    // Destination
    public string DestinationPath { get; set; } = "";
    public string? NetworkUsername { get; set; }
    public string? NetworkPassword { get; set; }

    // Options
    public BackupCompression Compression { get; set; } = BackupCompression.Normal;
    public bool EnableEncryption { get; set; } = false;
    public string? EncryptionPassword { get; set; }
    public bool VerifyAfterBackup { get; set; } = true;
    public bool UseVss { get; set; } = true; // Volume Shadow Copy for in-use files

    // Retention
    public int MaxBackupsToKeep { get; set; } = 5;
    public int MaxAgeDays { get; set; } = 90;

    // Metadata
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public string? Description { get; set; }
}

// ==================== BACKUP RESULTS ====================

/// <summary>
/// Result of a backup operation
/// </summary>
public record BackupResult
{
    public bool Success { get; init; }
    public BackupStatus Status { get; init; }
    public string Message { get; init; } = "";
    public string? OutputPath { get; init; }
    public long TotalBytes { get; init; }
    public long ProcessedBytes { get; init; }
    public int TotalFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public int SkippedFiles { get; init; }
    public int FailedFiles { get; init; }
    public TimeSpan Duration { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public List<BackupError> Errors { get; init; } = [];
    public BackupArchive? Archive { get; init; }
}

/// <summary>
/// Progress information during backup
/// </summary>
public record BackupProgress
{
    public BackupStatus Status { get; init; }
    public string CurrentFile { get; init; } = "";
    public string CurrentOperation { get; init; } = "";
    public long TotalBytes { get; init; }
    public long ProcessedBytes { get; init; }
    public int TotalFiles { get; init; }
    public int ProcessedFiles { get; init; }
    public double PercentComplete => TotalBytes > 0 ? (ProcessedBytes * 100.0 / TotalBytes) : 0;
    public double FilesPercentComplete => TotalFiles > 0 ? (ProcessedFiles * 100.0 / TotalFiles) : 0;
    public TimeSpan Elapsed { get; init; }
    public TimeSpan? EstimatedTimeRemaining { get; init; }
    public long BytesPerSecond { get; init; }
    public string FormattedSpeed => FormatSpeed(BytesPerSecond);

    private static string FormatSpeed(long bytesPerSecond)
    {
        if (bytesPerSecond >= 1_000_000_000) return $"{bytesPerSecond / 1_000_000_000.0:F1} GB/s";
        if (bytesPerSecond >= 1_000_000) return $"{bytesPerSecond / 1_000_000.0:F1} MB/s";
        if (bytesPerSecond >= 1_000) return $"{bytesPerSecond / 1_000.0:F1} KB/s";
        return $"{bytesPerSecond} B/s";
    }
}

/// <summary>
/// Error that occurred during backup
/// </summary>
public record BackupError
{
    public string FilePath { get; init; } = "";
    public string ErrorMessage { get; init; } = "";
    public string? ErrorCode { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

// ==================== BACKUP ARCHIVE ====================

/// <summary>
/// Information about a completed backup archive
/// </summary>
public class BackupArchive
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public string FilePath { get; set; } = "";
    public BackupType Type { get; set; }
    public DateTime CreatedDate { get; set; }
    public long SizeBytes { get; set; }
    public string FormattedSize => FormatSize(SizeBytes);
    public int FileCount { get; set; }
    public bool IsEncrypted { get; set; }
    public bool IsVerified { get; set; }
    public string? Description { get; set; }
    public List<string> SourcePaths { get; set; } = [];
    public string? ParentBackupId { get; set; } // For incremental backups
    public BackupManifest? Manifest { get; set; }

    private static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F1} {sizes[order]}";
    }
}

/// <summary>
/// Manifest of files in a backup archive
/// </summary>
public class BackupManifest
{
    public string BackupId { get; set; } = "";
    public DateTime CreatedDate { get; set; }
    public List<BackupFileEntry> Files { get; set; } = [];
    public Dictionary<string, string> Metadata { get; set; } = [];
}

/// <summary>
/// Entry for a single file in the backup
/// </summary>
public record BackupFileEntry
{
    public string RelativePath { get; init; } = "";
    public string OriginalPath { get; init; } = "";
    public long SizeBytes { get; init; }
    public DateTime ModifiedDate { get; init; }
    public string Hash { get; init; } = ""; // SHA256 for verification
    public FileAttributes Attributes { get; init; }
}

// ==================== RESTORE ====================

/// <summary>
/// Options for restore operation
/// </summary>
public class RestoreOptions
{
    public bool OverwriteExisting { get; set; } = false;
    public bool PreservePermissions { get; set; } = true;
    public bool RestoreToOriginalLocation { get; set; } = true;
    public string? AlternateDestination { get; set; }
    public List<string>? SelectiveFiles { get; set; } // null = restore all
    public bool VerifyAfterRestore { get; set; } = true;
}

/// <summary>
/// Windows System Restore Point information
/// </summary>
public record RestorePointInfo
{
    public int SequenceNumber { get; init; }
    public string Description { get; init; } = "";
    public DateTime CreationTime { get; init; }
    public string RestorePointType { get; init; } = "";
}

// ==================== SCHEDULING ====================

/// <summary>
/// Scheduled backup configuration
/// </summary>
public class BackupSchedule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public BackupJob Job { get; set; } = new();
    public BackupFrequency Frequency { get; set; } = BackupFrequency.Weekly;
    public TimeSpan TimeOfDay { get; set; } = new(2, 0, 0); // 2 AM default
    public DayOfWeek? DayOfWeek { get; set; } = System.DayOfWeek.Sunday;
    public int? DayOfMonth { get; set; }
    public DateTime? NextRunTime { get; set; }
    public DateTime? LastRunTime { get; set; }
    public BackupResult? LastResult { get; set; }
    public bool NotifyOnCompletion { get; set; } = true;
    public bool NotifyOnFailure { get; set; } = true;
}

// ==================== DRIVE INFO ====================

/// <summary>
/// Information about available space on a drive
/// </summary>
public record DriveSpaceInfo
{
    public string DrivePath { get; init; } = "";
    public string DriveLabel { get; init; } = "";
    public string DriveType { get; init; } = "";
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }
    public long UsedBytes => TotalBytes - FreeBytes;
    public double FreePercent => TotalBytes > 0 ? (FreeBytes * 100.0 / TotalBytes) : 0;
    public string FormattedTotal => FormatSize(TotalBytes);
    public string FormattedFree => FormatSize(FreeBytes);
    public string FormattedUsed => FormatSize(UsedBytes);
    public bool IsReady { get; init; }
    public bool IsRemovable { get; init; }

    private static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F1} {sizes[order]}";
    }
}
