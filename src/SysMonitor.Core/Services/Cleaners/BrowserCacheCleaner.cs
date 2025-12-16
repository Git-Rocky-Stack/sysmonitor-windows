using Microsoft.Extensions.Logging;
using SysMonitor.Core.Helpers;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Cleaners;

public class BrowserCacheCleaner : IBrowserCacheCleaner
{
    private record BrowserCacheLocation(string BrowserName, string DisplayName, string CachePath);

    private readonly ILogger<BrowserCacheCleaner> _logger;
    private readonly List<BrowserCacheLocation> _cacheLocations;

    public BrowserCacheCleaner(ILogger<BrowserCacheCleaner> logger)
    {
        _logger = logger;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        _cacheLocations = new List<BrowserCacheLocation>
        {
            // Google Chrome (modern Chromium-based paths with Cache_Data subfolder)
            new("Chrome", "Google Chrome Cache", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data")),
            new("Chrome", "Google Chrome Code Cache", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Code Cache")),
            new("Chrome", "Google Chrome GPU Cache", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "GPUCache")),
            new("Chrome", "Google Chrome Service Worker", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Service Worker", "CacheStorage")),

            // Microsoft Edge (modern Chromium-based paths with Cache_Data subfolder)
            new("Edge", "Microsoft Edge Cache", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache", "Cache_Data")),
            new("Edge", "Microsoft Edge Code Cache", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Code Cache")),
            new("Edge", "Microsoft Edge GPU Cache", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "GPUCache")),
            new("Edge", "Microsoft Edge Service Worker", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Service Worker", "CacheStorage")),

            // Firefox (handled specially in scan)
            new("Firefox", "Mozilla Firefox Cache", Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles")),

            // Brave (modern Chromium-based paths with Cache_Data subfolder)
            new("Brave", "Brave Cache", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache", "Cache_Data")),
            new("Brave", "Brave Code Cache", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Code Cache")),
            new("Brave", "Brave GPU Cache", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "GPUCache")),

            // Opera (modern Chromium-based paths with Cache_Data subfolder)
            new("Opera", "Opera Cache", Path.Combine(localAppData, "Opera Software", "Opera Stable", "Cache", "Cache_Data")),
            new("Opera", "Opera Code Cache", Path.Combine(localAppData, "Opera Software", "Opera Stable", "Code Cache")),

            // Vivaldi (modern Chromium-based paths with Cache_Data subfolder)
            new("Vivaldi", "Vivaldi Cache", Path.Combine(localAppData, "Vivaldi", "User Data", "Default", "Cache", "Cache_Data")),
        };
    }

    public async Task<List<CleanerScanResult>> ScanAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<CleanerScanResult>();
            var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var location in _cacheLocations)
            {
                try
                {
                    // Firefox needs special handling for profile folders
                    if (location.BrowserName == "Firefox")
                    {
                        if (!Directory.Exists(location.CachePath)) continue;

                        foreach (var profile in Directory.GetDirectories(location.CachePath))
                        {
                            var cachePath = Path.Combine(profile, "cache2");
                            if (Directory.Exists(cachePath) && !processedPaths.Contains(cachePath))
                            {
                                processedPaths.Add(cachePath);
                                var (size, count) = GetDirectorySize(cachePath);
                                if (size > 0)
                                {
                                    results.Add(new CleanerScanResult
                                    {
                                        Category = CleanerCategory.BrowserCache,
                                        Name = location.DisplayName,
                                        Path = cachePath,
                                        SizeBytes = size,
                                        FileCount = count,
                                        IsSelected = true,
                                        RiskLevel = CleanerRiskLevel.Safe,
                                        Description = $"{count} files, {FormatHelper.FormatSize(size)}"
                                    });
                                }
                            }
                        }
                    }
                    else
                    {
                        if (!Directory.Exists(location.CachePath)) continue;
                        if (processedPaths.Contains(location.CachePath)) continue;

                        processedPaths.Add(location.CachePath);
                        var (size, count) = GetDirectorySize(location.CachePath);
                        if (size > 0)
                        {
                            results.Add(new CleanerScanResult
                            {
                                Category = CleanerCategory.BrowserCache,
                                Name = location.DisplayName,
                                Path = location.CachePath,
                                SizeBytes = size,
                                FileCount = count,
                                IsSelected = true,
                                RiskLevel = CleanerRiskLevel.Safe,
                                Description = $"{count} files, {FormatHelper.FormatSize(size)}"
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to scan browser cache at {Path}", location.CachePath);
                }
            }

            // Group results by browser for better organization
            return results.OrderBy(r => r.Name).ToList();
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
                    if (!Directory.Exists(item.Path)) continue;

                    foreach (var file in Directory.EnumerateFiles(item.Path, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            var size = fileInfo.Length;
                            fileInfo.Delete();
                            result.BytesCleaned += size;
                            result.FilesDeleted++;
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            result.ErrorCount++;
                            _logger.LogTrace(ex, "Access denied to {File}", file);
                        }
                        catch (IOException ex)
                        {
                            result.ErrorCount++;
                            _logger.LogTrace(ex, "IO error for {File}", file);
                        }
                        catch (Exception ex)
                        {
                            result.ErrorCount++;
                            result.Errors.Add($"{file}: {ex.Message}");
                            _logger.LogDebug(ex, "Failed to delete {File}", file);
                        }
                    }

                    // Try to delete empty subdirectories
                    foreach (var dir in Directory.GetDirectories(item.Path))
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                            result.FoldersDeleted++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogTrace(ex, "Failed to delete directory {Dir}", dir);
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"{item.Path}: {ex.Message}");
                    result.ErrorCount++;
                    _logger.LogWarning(ex, "Failed to clean browser cache at {Path}", item.Path);
                }
            }

            result.Duration = DateTime.Now - startTime;
            result.Success = result.ErrorCount < result.FilesDeleted; // Allow some errors
            _logger.LogInformation("Cleaned {FilesDeleted} browser cache files ({BytesCleaned} bytes)",
                result.FilesDeleted, result.BytesCleaned);
            return result;
        });
    }

    private static (long size, int count) GetDirectorySize(string path)
    {
        long size = 0;
        int count = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    size += new FileInfo(file).Length;
                    count++;
                }
                catch
                {
                    // Expected for locked/inaccessible files - silently skip
                }
            }
        }
        catch
        {
            // Expected for inaccessible directories - silently skip
        }
        return (size, count);
    }
}
