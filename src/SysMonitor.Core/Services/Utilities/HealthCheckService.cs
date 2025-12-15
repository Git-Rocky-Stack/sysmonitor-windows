using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Optimizers;

namespace SysMonitor.Core.Services.Utilities;

public interface IHealthCheckService
{
    Task<HealthCheckReport> RunFullScanAsync(IProgress<HealthCheckProgress>? progress = null,
        CancellationToken cancellationToken = default);
    Task<QuickFixResult> ApplyQuickFixAsync(HealthCheckReport report,
        IProgress<HealthCheckProgress>? progress = null, CancellationToken cancellationToken = default);
    int CalculateHealthScore(HealthCheckReport report);
}

public class HealthCheckProgress
{
    public string CurrentTask { get; set; } = string.Empty;
    public int TasksCompleted { get; set; }
    public int TotalTasks { get; set; }
    public double PercentComplete => TotalTasks > 0 ? (double)TasksCompleted / TotalTasks * 100 : 0;
}

public class HealthCheckReport
{
    public DateTime ScanTime { get; set; } = DateTime.Now;
    public TimeSpan ScanDuration { get; set; }
    public int HealthScore { get; set; }
    public string HealthStatus { get; set; } = string.Empty;
    public string HealthColor { get; set; } = "#4CAF50";

    // Junk Files
    public long TotalJunkBytes { get; set; }
    public string FormattedJunkSize { get; set; } = string.Empty;
    public int JunkFileCount { get; set; }
    public List<CleanerScanResult> JunkFiles { get; set; } = new();

    // Browser Data
    public long TotalBrowserBytes { get; set; }
    public string FormattedBrowserSize { get; set; } = string.Empty;
    public int BrowserItemCount { get; set; }
    public List<CleanerScanResult> BrowserCache { get; set; } = new();

    // Registry Issues
    public int RegistryIssueCount { get; set; }
    public List<RegistryIssue> RegistryIssues { get; set; } = new();

    // Startup Items
    public int StartupItemCount { get; set; }
    public int HighImpactStartupCount { get; set; }
    public List<StartupItem> StartupItems { get; set; } = new();

    // Disk Space
    public double DiskUsagePercent { get; set; }
    public long DiskFreeBytes { get; set; }
    public string FormattedDiskFree { get; set; } = string.Empty;
    public bool IsDiskLow { get; set; }

    // Memory
    public double MemoryUsagePercent { get; set; }
    public bool IsMemoryHigh { get; set; }

    // Large Files (optional scan)
    public long TotalLargeFilesBytes { get; set; }
    public int LargeFileCount { get; set; }

    // Issues Summary
    public List<HealthIssue> Issues { get; set; } = new();
    public int CriticalIssueCount => Issues.Count(i => i.Severity == IssueSeverity.Critical);
    public int WarningCount => Issues.Count(i => i.Severity == IssueSeverity.Warning);
    public int InfoCount => Issues.Count(i => i.Severity == IssueSeverity.Info);
}

public class HealthIssue
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public IssueSeverity Severity { get; set; }
    public IssueCategory Category { get; set; }
    public string Icon { get; set; } = "\uE7BA";
    public bool CanAutoFix { get; set; }
    public string FixAction { get; set; } = string.Empty;
}

public enum IssueSeverity
{
    Info,
    Warning,
    Critical
}

public enum IssueCategory
{
    DiskSpace,
    JunkFiles,
    BrowserData,
    Registry,
    Startup,
    Memory,
    Performance
}

public class QuickFixResult
{
    public bool Success { get; set; }
    public long BytesCleaned { get; set; }
    public string FormattedBytesCleaned { get; set; } = string.Empty;
    public int IssuesFixed { get; set; }
    public int RegistryIssuesFixed { get; set; }
    public List<string> Actions { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

public class HealthCheckService : IHealthCheckService
{
    private readonly ITempFileCleaner _tempFileCleaner;
    private readonly IBrowserCacheCleaner _browserCacheCleaner;
    private readonly IRegistryCleaner _registryCleaner;
    private readonly IStartupOptimizer _startupOptimizer;
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly IDiskMonitor _diskMonitor;

    public HealthCheckService(
        ITempFileCleaner tempFileCleaner,
        IBrowserCacheCleaner browserCacheCleaner,
        IRegistryCleaner registryCleaner,
        IStartupOptimizer startupOptimizer,
        IMemoryMonitor memoryMonitor,
        IDiskMonitor diskMonitor)
    {
        _tempFileCleaner = tempFileCleaner;
        _browserCacheCleaner = browserCacheCleaner;
        _registryCleaner = registryCleaner;
        _startupOptimizer = startupOptimizer;
        _memoryMonitor = memoryMonitor;
        _diskMonitor = diskMonitor;
    }

    public async Task<HealthCheckReport> RunFullScanAsync(IProgress<HealthCheckProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var report = new HealthCheckReport();
        var startTime = DateTime.Now;
        const int totalTasks = 6;
        var tasksCompleted = 0;

        try
        {
            // 1. Scan Junk Files
            progress?.Report(new HealthCheckProgress
            {
                CurrentTask = "Scanning junk files...",
                TasksCompleted = tasksCompleted,
                TotalTasks = totalTasks
            });

            report.JunkFiles = await _tempFileCleaner.ScanAsync();
            report.TotalJunkBytes = report.JunkFiles.Sum(j => j.SizeBytes);
            report.JunkFileCount = report.JunkFiles.Sum(j => j.FileCount);
            report.FormattedJunkSize = FormatSize(report.TotalJunkBytes);
            tasksCompleted++;

            cancellationToken.ThrowIfCancellationRequested();

            // 2. Scan Browser Cache
            progress?.Report(new HealthCheckProgress
            {
                CurrentTask = "Scanning browser data...",
                TasksCompleted = tasksCompleted,
                TotalTasks = totalTasks
            });

            report.BrowserCache = await _browserCacheCleaner.ScanAsync();
            report.TotalBrowserBytes = report.BrowserCache.Sum(b => b.SizeBytes);
            report.BrowserItemCount = report.BrowserCache.Sum(b => b.FileCount);
            report.FormattedBrowserSize = FormatSize(report.TotalBrowserBytes);
            tasksCompleted++;

            cancellationToken.ThrowIfCancellationRequested();

            // 3. Scan Registry
            progress?.Report(new HealthCheckProgress
            {
                CurrentTask = "Scanning registry...",
                TasksCompleted = tasksCompleted,
                TotalTasks = totalTasks
            });

            report.RegistryIssues = await _registryCleaner.ScanAsync();
            report.RegistryIssueCount = report.RegistryIssues.Count;
            tasksCompleted++;

            cancellationToken.ThrowIfCancellationRequested();

            // 4. Check Startup Items
            progress?.Report(new HealthCheckProgress
            {
                CurrentTask = "Analyzing startup programs...",
                TasksCompleted = tasksCompleted,
                TotalTasks = totalTasks
            });

            report.StartupItems = await _startupOptimizer.GetStartupItemsAsync();
            report.StartupItemCount = report.StartupItems.Count;
            report.HighImpactStartupCount = report.StartupItems.Count(s => s.Impact == StartupImpact.High);
            tasksCompleted++;

            cancellationToken.ThrowIfCancellationRequested();

            // 5. Check Disk Space
            progress?.Report(new HealthCheckProgress
            {
                CurrentTask = "Checking disk space...",
                TasksCompleted = tasksCompleted,
                TotalTasks = totalTasks
            });

            var diskInfo = await _diskMonitor.GetAllDisksAsync();
            if (diskInfo.Count > 0)
            {
                var systemDrive = diskInfo.FirstOrDefault(d => d.Name.StartsWith("C")) ?? diskInfo[0];
                report.DiskUsagePercent = systemDrive.UsagePercent;
                report.DiskFreeBytes = systemDrive.FreeBytes;
                var freeGB = systemDrive.FreeBytes / 1_073_741_824.0;
                report.FormattedDiskFree = $"{freeGB:F1} GB";
                report.IsDiskLow = freeGB < 10; // Less than 10GB free
            }
            tasksCompleted++;

            cancellationToken.ThrowIfCancellationRequested();

            // 6. Check Memory
            progress?.Report(new HealthCheckProgress
            {
                CurrentTask = "Checking memory usage...",
                TasksCompleted = tasksCompleted,
                TotalTasks = totalTasks
            });

            var memoryInfo = await _memoryMonitor.GetMemoryInfoAsync();
            report.MemoryUsagePercent = memoryInfo.UsagePercent;
            report.IsMemoryHigh = memoryInfo.UsagePercent > 85;
            tasksCompleted++;

            // Generate Issues List
            GenerateIssuesList(report);

            // Calculate Health Score
            report.HealthScore = CalculateHealthScore(report);
            (report.HealthStatus, report.HealthColor) = GetHealthStatusAndColor(report.HealthScore);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Continue with partial results
        }

        report.ScanDuration = DateTime.Now - startTime;

        progress?.Report(new HealthCheckProgress
        {
            CurrentTask = "Scan complete!",
            TasksCompleted = totalTasks,
            TotalTasks = totalTasks
        });

        return report;
    }

    public async Task<QuickFixResult> ApplyQuickFixAsync(HealthCheckReport report,
        IProgress<HealthCheckProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new QuickFixResult { Success = true };
        var startTime = DateTime.Now;
        const int totalTasks = 3;
        var tasksCompleted = 0;

        try
        {
            // 1. Clean Junk Files
            progress?.Report(new HealthCheckProgress
            {
                CurrentTask = "Cleaning junk files...",
                TasksCompleted = tasksCompleted,
                TotalTasks = totalTasks
            });

            if (report.JunkFiles.Any(j => j.IsSelected))
            {
                var junkResult = await _tempFileCleaner.CleanAsync(report.JunkFiles.Where(j => j.IsSelected));
                result.BytesCleaned += junkResult.BytesCleaned;
                result.Actions.Add($"Cleaned {FormatSize(junkResult.BytesCleaned)} of junk files");
            }
            tasksCompleted++;

            cancellationToken.ThrowIfCancellationRequested();

            // 2. Clean Browser Cache
            progress?.Report(new HealthCheckProgress
            {
                CurrentTask = "Cleaning browser cache...",
                TasksCompleted = tasksCompleted,
                TotalTasks = totalTasks
            });

            if (report.BrowserCache.Any(b => b.IsSelected))
            {
                var browserResult = await _browserCacheCleaner.CleanAsync(report.BrowserCache.Where(b => b.IsSelected));
                result.BytesCleaned += browserResult.BytesCleaned;
                result.Actions.Add($"Cleaned {FormatSize(browserResult.BytesCleaned)} of browser cache");
            }
            tasksCompleted++;

            cancellationToken.ThrowIfCancellationRequested();

            // 3. Fix Registry Issues (safe ones only)
            progress?.Report(new HealthCheckProgress
            {
                CurrentTask = "Fixing registry issues...",
                TasksCompleted = tasksCompleted,
                TotalTasks = totalTasks
            });

            var safeRegistryIssues = report.RegistryIssues
                .Where(r => r.RiskLevel == CleanerRiskLevel.Safe || r.RiskLevel == CleanerRiskLevel.Low)
                .ToList();

            if (safeRegistryIssues.Any())
            {
                var registryResult = await _registryCleaner.CleanAsync(safeRegistryIssues);
                result.RegistryIssuesFixed = safeRegistryIssues.Count - registryResult.ErrorCount;
                result.Actions.Add($"Fixed {result.RegistryIssuesFixed} registry issues");
            }
            tasksCompleted++;

            result.IssuesFixed = result.Actions.Count;
            result.FormattedBytesCleaned = FormatSize(result.BytesCleaned);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add(ex.Message);
        }

        result.Duration = DateTime.Now - startTime;

        progress?.Report(new HealthCheckProgress
        {
            CurrentTask = "Quick fix complete!",
            TasksCompleted = totalTasks,
            TotalTasks = totalTasks
        });

        return result;
    }

    public int CalculateHealthScore(HealthCheckReport report)
    {
        var score = 100;

        // Deduct for junk files (up to -15 points)
        if (report.TotalJunkBytes > 5_000_000_000) score -= 15; // > 5GB
        else if (report.TotalJunkBytes > 1_000_000_000) score -= 10; // > 1GB
        else if (report.TotalJunkBytes > 500_000_000) score -= 5; // > 500MB

        // Deduct for browser cache (up to -10 points)
        if (report.TotalBrowserBytes > 2_000_000_000) score -= 10; // > 2GB
        else if (report.TotalBrowserBytes > 500_000_000) score -= 5; // > 500MB

        // Deduct for registry issues (up to -15 points)
        if (report.RegistryIssueCount > 100) score -= 15;
        else if (report.RegistryIssueCount > 50) score -= 10;
        else if (report.RegistryIssueCount > 20) score -= 5;

        // Deduct for startup items (up to -10 points)
        if (report.StartupItemCount > 15) score -= 10;
        else if (report.StartupItemCount > 10) score -= 5;

        // Deduct for disk space (up to -20 points)
        if (report.IsDiskLow) score -= 20;
        else if (report.DiskUsagePercent > 90) score -= 15;
        else if (report.DiskUsagePercent > 80) score -= 10;

        // Deduct for memory usage (up to -10 points)
        if (report.IsMemoryHigh) score -= 10;
        else if (report.MemoryUsagePercent > 80) score -= 5;

        return Math.Max(0, Math.Min(100, score));
    }

    private void GenerateIssuesList(HealthCheckReport report)
    {
        report.Issues.Clear();

        // Disk space issues
        if (report.IsDiskLow)
        {
            report.Issues.Add(new HealthIssue
            {
                Title = "Low Disk Space",
                Description = $"Only {report.FormattedDiskFree} free on system drive. Consider cleaning up files.",
                Severity = IssueSeverity.Critical,
                Category = IssueCategory.DiskSpace,
                Icon = "\uEDA2",
                CanAutoFix = false
            });
        }
        else if (report.DiskUsagePercent > 85)
        {
            report.Issues.Add(new HealthIssue
            {
                Title = "Disk Space Running Low",
                Description = $"System drive is {report.DiskUsagePercent:F0}% full.",
                Severity = IssueSeverity.Warning,
                Category = IssueCategory.DiskSpace,
                Icon = "\uEDA2",
                CanAutoFix = false
            });
        }

        // Junk files
        if (report.TotalJunkBytes > 1_000_000_000)
        {
            report.Issues.Add(new HealthIssue
            {
                Title = "Large Amount of Junk Files",
                Description = $"{report.FormattedJunkSize} of temporary and junk files found.",
                Severity = report.TotalJunkBytes > 5_000_000_000 ? IssueSeverity.Warning : IssueSeverity.Info,
                Category = IssueCategory.JunkFiles,
                Icon = "\uE74D",
                CanAutoFix = true,
                FixAction = "CleanJunk"
            });
        }

        // Browser data
        if (report.TotalBrowserBytes > 500_000_000)
        {
            report.Issues.Add(new HealthIssue
            {
                Title = "Browser Cache Buildup",
                Description = $"{report.FormattedBrowserSize} of browser cache data.",
                Severity = IssueSeverity.Info,
                Category = IssueCategory.BrowserData,
                Icon = "\uE774",
                CanAutoFix = true,
                FixAction = "CleanBrowser"
            });
        }

        // Registry issues
        if (report.RegistryIssueCount > 20)
        {
            report.Issues.Add(new HealthIssue
            {
                Title = "Registry Issues Found",
                Description = $"{report.RegistryIssueCount} registry issues detected.",
                Severity = report.RegistryIssueCount > 100 ? IssueSeverity.Warning : IssueSeverity.Info,
                Category = IssueCategory.Registry,
                Icon = "\uE8F1",
                CanAutoFix = true,
                FixAction = "CleanRegistry"
            });
        }

        // Startup items
        if (report.StartupItemCount > 10)
        {
            report.Issues.Add(new HealthIssue
            {
                Title = "Many Startup Programs",
                Description = $"{report.StartupItemCount} programs run at startup, which may slow boot time.",
                Severity = IssueSeverity.Info,
                Category = IssueCategory.Startup,
                Icon = "\uE7E8",
                CanAutoFix = false
            });
        }

        // Memory usage
        if (report.IsMemoryHigh)
        {
            report.Issues.Add(new HealthIssue
            {
                Title = "High Memory Usage",
                Description = $"Memory usage is at {report.MemoryUsagePercent:F0}%. Consider closing some applications.",
                Severity = IssueSeverity.Warning,
                Category = IssueCategory.Memory,
                Icon = "\uE964",
                CanAutoFix = false
            });
        }
    }

    private static (string status, string color) GetHealthStatusAndColor(int score) => score switch
    {
        >= 90 => ("Excellent", "#4CAF50"),
        >= 75 => ("Good", "#8BC34A"),
        >= 60 => ("Fair", "#FF9800"),
        >= 40 => ("Poor", "#FF5722"),
        _ => ("Critical", "#F44336")
    };

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}
