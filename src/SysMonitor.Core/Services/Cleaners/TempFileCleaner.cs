using SysMonitor.Core.Models;
using System.Runtime.InteropServices;

namespace SysMonitor.Core.Services.Cleaners;

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

    private readonly Dictionary<CleanerCategory, (string Path, string Description)> _cleanLocations;

    public TempFileCleaner()
    {
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

    public async Task<List<CleanerScanResult>> ScanAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<CleanerScanResult>();
            foreach (var (category, (path, description)) in _cleanLocations)
            {
                try
                {
                    if (!Directory.Exists(path)) continue;
                    var (size, count) = GetDirectorySize(path, category);
                    if (size > 0)
                    {
                        results.Add(new CleanerScanResult
                        {
                            Category = category,
                            Name = description,
                            Path = path,
                            SizeBytes = size,
                            FileCount = count,
                            IsSelected = category != CleanerCategory.LogFiles, // Don't auto-select logs
                            RiskLevel = GetRiskLevel(category),
                            Description = $"{count} files, {FormatSize(size)}"
                        });
                    }
                }
                catch { }
            }

            // Recycle Bin - use Shell API for reliable size query
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
                        Description = $"{rbInfo.i64NumItems:N0} items, {FormatSize(rbInfo.i64Size)}"
                    });
                }
            }
            catch { }

            return results;
        });
    }

    public async Task<CleanerResult> CleanAsync(IEnumerable<CleanerScanResult> itemsToClean)
    {
        return await Task.Run(() =>
        {
            var result = new CleanerResult { Success = true };
            var startTime = DateTime.Now;

            foreach (var item in itemsToClean.Where(i => i.IsSelected))
            {
                try
                {
                    if (item.Category == CleanerCategory.RecycleBin)
                    {
                        // Actually empty the Recycle Bin using Shell API
                        var hr = SHEmptyRecycleBin(IntPtr.Zero, null,
                            SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);

                        // S_OK (0) or S_FALSE (1) both indicate success
                        // E_UNEXPECTED (-2147418113) means bin is already empty
                        if (hr == 0 || hr == 1 || hr == -2147418113)
                        {
                            result.BytesCleaned += item.SizeBytes;
                            result.FilesDeleted += item.FileCount;
                        }
                        else
                        {
                            result.ErrorCount++;
                            result.Errors.Add($"Recycle Bin: Failed to empty (error code: {hr})");
                        }
                        continue;
                    }

                    if (!Directory.Exists(item.Path)) continue;

                    // Clean files in directory
                    foreach (var file in Directory.EnumerateFiles(item.Path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            var size = fileInfo.Length;

                            // Skip files that are too new (might be in use)
                            if (item.Category == CleanerCategory.LogFiles &&
                                fileInfo.LastWriteTime > DateTime.Now.AddDays(-1))
                                continue;

                            // Skip thumbnail database files that are often locked by Explorer
                            if (item.Category == CleanerCategory.Thumbnails)
                            {
                                var fileName = fileInfo.Name.ToLowerInvariant();
                                if (fileName.StartsWith("thumbcache_") && fileName.EndsWith(".db"))
                                    continue; // These are locked by Explorer
                                if (fileName == "iconcache_idx.db" || fileName.StartsWith("iconcache_"))
                                    continue;
                            }

                            // Skip very recent temp files (might be in active use)
                            // Use LastWriteTime instead of LastAccessTime since antivirus and indexing
                            // services constantly touch files, making LastAccessTime unreliable
                            if ((item.Category == CleanerCategory.UserTemp ||
                                 item.Category == CleanerCategory.WindowsTemp) &&
                                fileInfo.LastWriteTime > DateTime.Now.AddMinutes(-5))
                                continue;

                            // Try to remove read-only attribute if set
                            if (fileInfo.IsReadOnly)
                            {
                                fileInfo.IsReadOnly = false;
                            }

                            fileInfo.Delete();
                            result.BytesCleaned += size;
                            result.FilesDeleted++;
                        }
                        catch (UnauthorizedAccessException) { result.ErrorCount++; }
                        catch (IOException) { result.ErrorCount++; }
                        catch (Exception ex)
                        {
                            result.ErrorCount++;
                            result.Errors.Add($"{file}: {ex.Message}");
                        }
                    }

                    // Clean empty subdirectories (except for root cleanup folders)
                    if (item.Category != CleanerCategory.LogFiles)
                    {
                        foreach (var dir in Directory.GetDirectories(item.Path))
                        {
                            try
                            {
                                Directory.Delete(dir, true);
                                result.FoldersDeleted++;
                            }
                            catch { }
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{item.Path}: {ex.Message}");
                    result.ErrorCount++;
                }
            }

            result.Duration = DateTime.Now - startTime;
            result.Success = result.ErrorCount < result.FilesDeleted; // Allow some errors
            return result;
        });
    }

    public async Task<long> GetTotalCleanableBytesAsync()
    {
        var results = await ScanAsync();
        return results.Sum(r => r.SizeBytes);
    }

    private static (long size, int count) GetDirectorySize(string path, CleanerCategory? category = null)
    {
        long size = 0;
        int count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
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
                        fileInfo.LastWriteTime > DateTime.Now.AddDays(-1))
                        continue;

                    // Skip very recent temp files (matches CleanAsync behavior)
                    // Use LastWriteTime instead of LastAccessTime since antivirus and indexing
                    // services constantly touch files, making LastAccessTime unreliable
                    if ((category == CleanerCategory.UserTemp ||
                         category == CleanerCategory.WindowsTemp) &&
                        fileInfo.LastWriteTime > DateTime.Now.AddMinutes(-5))
                        continue;

                    size += fileInfo.Length;
                    count++;
                }
                catch { }
            }
        }
        catch { }
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

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824)
            return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}
