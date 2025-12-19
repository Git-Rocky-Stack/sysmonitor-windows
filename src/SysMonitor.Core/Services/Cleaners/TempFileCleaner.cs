using Microsoft.Extensions.Logging;
using SysMonitor.Core.Helpers;
using SysMonitor.Core.Models;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace SysMonitor.Core.Services.Cleaners;

/// <summary>
/// Optimized temp file cleaner with parallel scanning and async operations.
///
/// PERFORMANCE OPTIMIZATIONS:
/// 1. Parallel directory scanning using Parallel.ForEach
/// 2. Async file enumeration to prevent UI blocking
/// 3. Batch file deletion with configurable parallelism
/// 4. Streaming enumeration instead of loading all files into memory
/// 5. Pre-filtered file enumeration to skip known locked files
/// </summary>
public class TempFileCleaner : ITempFileCleaner
{
    // Shell API for emptying Recycle Bin
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    // Shell API for querying Recycle Bin size
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBin(string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    private const uint SHERB_NOCONFIRMATION = 0x00000001;
    private const uint SHERB_NOPROGRESSUI = 0x00000002;
    private const uint SHERB_NOSOUND = 0x00000004;

    // Parallelism configuration
    private static readonly int MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2);

    private readonly ILogger<TempFileCleaner> _logger;
    private readonly Dictionary<CleanerCategory, (string Path, string Description)> _cleanLocations;

    public TempFileCleaner(ILogger<TempFileCleaner> logger)
    {
        _logger = logger;
        var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var windowsTemp = Path.Combine(windowsPath, "Temp");
        var userTemp = Path.GetTempPath();
        var prefetch = Path.Combine(windowsPath, "Prefetch");
        var thumbnails = Path.Combine(localAppData, "Microsoft", "Windows", "Explorer");

        // Windows Update Cache
        var softwareDistribution = Path.Combine(windowsPath, "SoftwareDistribution", "Download");

        // Memory Dumps
        var memoryDumps = Path.Combine(windowsPath, "Minidump");

        // Error Reports
        var errorReports = Path.Combine(localAppData, "Microsoft", "Windows", "WER", "ReportArchive");

        // Log Files
        var windowsLogs = Path.Combine(windowsPath, "Logs");

        _cleanLocations = new Dictionary<CleanerCategory, (string, string)>
        {
            { CleanerCategory.WindowsTemp, (windowsTemp, "Windows Temporary Files") },
            { CleanerCategory.UserTemp, (userTemp, "User Temporary Files") },
            { CleanerCategory.Prefetch, (prefetch, "Windows Prefetch Files") },
            { CleanerCategory.Thumbnails, (thumbnails, "Thumbnail Cache") },
            { CleanerCategory.WindowsUpdateCache, (softwareDistribution, "Windows Update Cache") },
            { CleanerCategory.MemoryDumps, (memoryDumps, "Memory Dump Files") },
            { CleanerCategory.ErrorReports, (errorReports, "Windows Error Reports") },
            { CleanerCategory.LogFiles, (windowsLogs, "Windows Log Files") }
        };
    }

    /// <summary>
    /// OPTIMIZATION: Parallel scanning of all cleaner locations.
    /// Scans multiple directories simultaneously for faster results.
    /// </summary>
    public async Task<List<CleanerScanResult>> ScanAsync()
    {
        var results = new ConcurrentBag<CleanerScanResult>();

        // Scan all locations in parallel
        await Task.Run(() =>
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism
            };

            Parallel.ForEach(_cleanLocations, parallelOptions, kvp =>
            {
                var (category, (path, description)) = kvp;
                try
                {
                    if (!Directory.Exists(path)) return;

                    var (size, count) = GetDirectorySizeOptimized(path, category);
                    if (size > 0)
                    {
                        results.Add(new CleanerScanResult
                        {
                            Category = category,
                            Name = description,
                            Path = path,
                            SizeBytes = size,
                            FileCount = count,
                            IsSelected = category != CleanerCategory.LogFiles,
                            RiskLevel = GetRiskLevel(category),
                            Description = $"{count} files, {FormatHelper.FormatSize(size)}"
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to scan {Category} at {Path}", category, path);
                }
            });
        });

        // Recycle Bin - use Shell API for reliable size query (must be on main thread)
        try
        {
            var rbInfo = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
            var hr = SHQueryRecycleBin(null, ref rbInfo);

            if (hr == 0 && rbInfo.i64Size > 0)
            {
                results.Add(new CleanerScanResult
                {
                    Category = CleanerCategory.RecycleBin,
                    Name = "Recycle Bin",
                    Path = "All Drives",
                    SizeBytes = rbInfo.i64Size,
                    FileCount = (int)rbInfo.i64NumItems,
                    IsSelected = true,
                    RiskLevel = CleanerRiskLevel.Safe,
                    Description = $"{rbInfo.i64NumItems:N0} items, {FormatHelper.FormatSize(rbInfo.i64Size)}"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to query Recycle Bin");
        }

        return results.ToList();
    }

    /// <summary>
    /// OPTIMIZATION: Parallel file deletion with batched operations.
    /// Uses Parallel.ForEach for faster cleaning of large directories.
    /// </summary>
    public async Task<CleanerResult> CleanAsync(IEnumerable<CleanerScanResult> itemsToClean)
    {
        var result = new CleanerResult { Success = true };
        var startTime = DateTime.Now;

        // Thread-safe counters for parallel operations
        long bytesCleaned = 0;
        int filesDeleted = 0;
        int foldersDeleted = 0;
        int errorCount = 0;
        var errors = new ConcurrentBag<string>();

        var selectedItems = itemsToClean.Where(i => i.IsSelected).ToList();

        // Process items in parallel (except RecycleBin which uses Shell API)
        await Task.Run(() =>
        {
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = MaxDegreeOfParallelism
            };

            Parallel.ForEach(selectedItems, parallelOptions, item =>
            {
                try
                {
                    if (item.Category == CleanerCategory.RecycleBin)
                    {
                        // Shell API call - thread-safe
                        var hr = SHEmptyRecycleBin(IntPtr.Zero, null,
                            SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

                        if (hr == 0 || hr == 1 || hr == -2147418113)
                        {
                            Interlocked.Add(ref bytesCleaned, item.SizeBytes);
                            Interlocked.Add(ref filesDeleted, item.FileCount);
                        }
                        else
                        {
                            Interlocked.Increment(ref errorCount);
                            errors.Add($"Recycle Bin: Failed to empty (error code: {hr})");
                        }
                        return;
                    }

                    if (!Directory.Exists(item.Path)) return;

                    // Get all files to process
                    var filesToDelete = GetCleanableFiles(item.Path, item.Category).ToList();

                    // Delete files in parallel batches
                    Parallel.ForEach(filesToDelete, new ParallelOptions { MaxDegreeOfParallelism = 4 }, file =>
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            var size = fileInfo.Length;

                            if (fileInfo.IsReadOnly)
                            {
                                fileInfo.IsReadOnly = false;
                            }

                            fileInfo.Delete();
                            Interlocked.Add(ref bytesCleaned, size);
                            Interlocked.Increment(ref filesDeleted);
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            _logger.LogTrace(ex, "Access denied to {File}", file);
                        }
                        catch (IOException ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            _logger.LogTrace(ex, "IO error for {File}", file);
                        }
                        catch (Exception ex)
                        {
                            Interlocked.Increment(ref errorCount);
                            errors.Add($"{file}: {ex.Message}");
                            _logger.LogDebug(ex, "Failed to delete {File}", file);
                        }
                    });

                    // Clean empty subdirectories (sequential to avoid conflicts)
                    if (item.Category != CleanerCategory.LogFiles)
                    {
                        foreach (var dir in Directory.GetDirectories(item.Path))
                        {
                            try
                            {
                                Directory.Delete(dir, true);
                                Interlocked.Increment(ref foldersDeleted);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogTrace(ex, "Failed to delete directory {Dir}", dir);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"{item.Path}: {ex.Message}");
                    Interlocked.Increment(ref errorCount);
                    _logger.LogWarning(ex, "Failed to clean {Category} at {Path}", item.Category, item.Path);
                }
            });
        });

        result.BytesCleaned = bytesCleaned;
        result.FilesDeleted = filesDeleted;
        result.FoldersDeleted = foldersDeleted;
        result.ErrorCount = errorCount;
        result.Errors = errors.ToList();
        result.Duration = DateTime.Now - startTime;
        result.Success = errorCount < filesDeleted;

        _logger.LogInformation("Cleaned {FilesDeleted} files ({BytesCleaned} bytes) with {ErrorCount} errors",
            filesDeleted, bytesCleaned, errorCount);
        return result;
    }

    /// <summary>
    /// OPTIMIZATION: Pre-filtered file enumeration that skips known locked files.
    /// Returns only files that are actually deletable based on category rules.
    /// </summary>
    private static IEnumerable<string> GetCleanableFiles(string path, CleanerCategory category)
    {
        var now = DateTime.Now;

        foreach (var file in EnumerateFilesSafe(path))
        {
            FileInfo? fileInfo = null;
            try
            {
                fileInfo = new FileInfo(file);
            }
            catch
            {
                continue;
            }

            // Skip files that are too new (might be in use)
            if (category == CleanerCategory.LogFiles &&
                fileInfo.LastWriteTime > now.AddDays(-1))
                continue;

            // Skip thumbnail database files that are often locked by Explorer
            if (category == CleanerCategory.Thumbnails)
            {
                var fileName = fileInfo.Name.ToLowerInvariant();
                if ((fileName.StartsWith("thumbcache_") && fileName.EndsWith(".db")) ||
                    fileName.StartsWith("iconcache_"))
                    continue;
            }

            // Skip very recent temp files (might be in active use)
            if ((category == CleanerCategory.UserTemp ||
                 category == CleanerCategory.WindowsTemp) &&
                fileInfo.LastWriteTime > now.AddMinutes(-5))
                continue;

            yield return file;
        }
    }

    /// <summary>
    /// Safe file enumeration that handles access denied errors gracefully.
    /// </summary>
    private static IEnumerable<string> EnumerateFilesSafe(string path)
    {
        var directories = new Stack<string>();
        directories.Push(path);

        while (directories.Count > 0)
        {
            var currentDir = directories.Pop();

            // Enumerate files in current directory
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(currentDir);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            // Add subdirectories
            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                {
                    directories.Push(subDir);
                }
            }
            catch
            {
                // Skip inaccessible directories
            }
        }
    }

    public async Task<long> GetTotalCleanableBytesAsync()
    {
        var results = await ScanAsync();
        return results.Sum(r => r.SizeBytes);
    }

    /// <summary>
    /// OPTIMIZATION: Uses streaming enumeration with safe directory traversal.
    /// Prevents blocking on large directories and handles access denied gracefully.
    /// </summary>
    private static (long size, int count) GetDirectorySizeOptimized(string path, CleanerCategory category)
    {
        long size = 0;
        int count = 0;
        var now = DateTime.Now;

        foreach (var file in EnumerateFilesSafe(path))
        {
            try
            {
                var fileInfo = new FileInfo(file);

                // Skip thumbnail database files that can't be cleaned
                if (category == CleanerCategory.Thumbnails)
                {
                    var fileName = fileInfo.Name.ToLowerInvariant();
                    if ((fileName.StartsWith("thumbcache_") && fileName.EndsWith(".db")) ||
                        fileName.StartsWith("iconcache_"))
                        continue;
                }

                // Skip log files modified within 1 day (matches CleanAsync behavior)
                if (category == CleanerCategory.LogFiles &&
                    fileInfo.LastWriteTime > now.AddDays(-1))
                    continue;

                // Skip very recent temp files (matches CleanAsync behavior)
                if ((category == CleanerCategory.UserTemp ||
                     category == CleanerCategory.WindowsTemp) &&
                    fileInfo.LastWriteTime > now.AddMinutes(-5))
                    continue;

                size += fileInfo.Length;
                count++;
            }
            catch
            {
                // Expected for locked/inaccessible files during scan - silently skip
            }
        }

        return (size, count);
    }

    private static CleanerRiskLevel GetRiskLevel(CleanerCategory category)
    {
        return category switch
        {
            CleanerCategory.UserTemp => CleanerRiskLevel.Safe,
            CleanerCategory.WindowsTemp => CleanerRiskLevel.Safe,
            CleanerCategory.Thumbnails => CleanerRiskLevel.Safe,
            CleanerCategory.RecycleBin => CleanerRiskLevel.Safe,
            CleanerCategory.Prefetch => CleanerRiskLevel.Low,
            CleanerCategory.WindowsUpdateCache => CleanerRiskLevel.Low,
            CleanerCategory.MemoryDumps => CleanerRiskLevel.Safe,
            CleanerCategory.ErrorReports => CleanerRiskLevel.Safe,
            CleanerCategory.LogFiles => CleanerRiskLevel.Low,
            _ => CleanerRiskLevel.Low
        };
    }
}
