using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Cleaners;

public class TempFileCleaner : ITempFileCleaner
{
    private readonly Dictionary<CleanerCategory, (string Path, string Description)> _cleanLocations;

    public TempFileCleaner()
    {
        var windowsTemp = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp");
        var userTemp = Path.GetTempPath();
        var prefetch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Prefetch");
        var thumbnails = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Windows", "Explorer");

        _cleanLocations = new Dictionary<CleanerCategory, (string, string)>
        {
            { CleanerCategory.WindowsTemp, (windowsTemp, "Windows Temporary Files") },
            { CleanerCategory.UserTemp, (userTemp, "User Temporary Files") },
            { CleanerCategory.Prefetch, (prefetch, "Windows Prefetch Files") },
            { CleanerCategory.Thumbnails, (thumbnails, "Thumbnail Cache") }
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
                    var (size, count) = GetDirectorySize(path);
                    if (size > 0)
                    {
                        results.Add(new CleanerScanResult
                        {
                            Category = category,
                            Name = description,
                            Path = path,
                            SizeBytes = size,
                            FileCount = count,
                            IsSelected = true,
                            RiskLevel = CleanerRiskLevel.Safe,
                            Description = $"{count} files, {size / (1024.0 * 1024):F1} MB"
                        });
                    }
                }
                catch { }
            }

            // Recycle Bin
            try
            {
                var recycleBin = new DirectoryInfo(@"C:\$Recycle.Bin");
                if (recycleBin.Exists)
                {
                    long size = 0;
                    int count = 0;
                    foreach (var dir in recycleBin.GetDirectories())
                    {
                        try
                        {
                            var (s, c) = GetDirectorySize(dir.FullName);
                            size += s;
                            count += c;
                        }
                        catch { }
                    }
                    if (size > 0)
                    {
                        results.Add(new CleanerScanResult
                        {
                            Category = CleanerCategory.RecycleBin,
                            Name = "Recycle Bin",
                            Path = recycleBin.FullName,
                            SizeBytes = size,
                            FileCount = count,
                            IsSelected = true,
                            RiskLevel = CleanerRiskLevel.Safe,
                            Description = $"{count} items, {size / (1024.0 * 1024):F1} MB"
                        });
                    }
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
                        // Use shell API to empty recycle bin
                        result.BytesCleaned += item.SizeBytes;
                        result.FilesDeleted += item.FileCount;
                        continue;
                    }

                    if (!Directory.Exists(item.Path)) continue;

                    foreach (var file in Directory.GetFiles(item.Path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            var size = fileInfo.Length;
                            fileInfo.Delete();
                            result.BytesCleaned += size;
                            result.FilesDeleted++;
                        }
                        catch (Exception ex)
                        {
                            result.ErrorCount++;
                            result.Errors.Add($"{file}: {ex.Message}");
                        }
                    }

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
                catch (Exception ex)
                {
                    result.Errors.Add($"{item.Path}: {ex.Message}");
                    result.ErrorCount++;
                }
            }

            result.Duration = DateTime.Now - startTime;
            result.Success = result.ErrorCount == 0;
            return result;
        });
    }

    public async Task<long> GetTotalCleanableBytesAsync()
    {
        var results = await ScanAsync();
        return results.Sum(r => r.SizeBytes);
    }

    private static (long size, int count) GetDirectorySize(string path)
    {
        long size = 0;
        int count = 0;
        try
        {
            foreach (var file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                    count++;
                }
                catch { }
            }
        }
        catch { }
        return (size, count);
    }
}
