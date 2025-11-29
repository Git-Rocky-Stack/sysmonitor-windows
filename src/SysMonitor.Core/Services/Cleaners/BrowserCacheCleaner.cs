using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Cleaners;

public class BrowserCacheCleaner : IBrowserCacheCleaner
{
    private record BrowserProfile(string Name, string CachePath);

    private readonly List<BrowserProfile> _browserProfiles;

    public BrowserCacheCleaner()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        _browserProfiles = new List<BrowserProfile>
        {
            new("Google Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data", "Default", "Cache")),
            new("Microsoft Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data", "Default", "Cache")),
            new("Mozilla Firefox", Path.Combine(localAppData, "Mozilla", "Firefox", "Profiles")),
            new("Brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data", "Default", "Cache")),
            new("Opera", Path.Combine(localAppData, "Opera Software", "Opera Stable", "Cache"))
        };
    }

    public async Task<List<CleanerScanResult>> ScanAsync()
    {
        return await Task.Run(() =>
        {
            var results = new List<CleanerScanResult>();
            foreach (var browser in _browserProfiles)
            {
                try
                {
                    if (!Directory.Exists(browser.CachePath)) continue;

                    // For Firefox, need to find profile folder
                    if (browser.Name == "Mozilla Firefox")
                    {
                        foreach (var profile in Directory.GetDirectories(browser.CachePath))
                        {
                            var cachePath = Path.Combine(profile, "cache2");
                            if (Directory.Exists(cachePath))
                            {
                                var (size, count) = GetDirectorySize(cachePath);
                                if (size > 0)
                                {
                                    results.Add(new CleanerScanResult
                                    {
                                        Category = CleanerCategory.BrowserCache,
                                        Name = $"{browser.Name} Cache",
                                        Path = cachePath,
                                        SizeBytes = size,
                                        FileCount = count,
                                        IsSelected = true,
                                        RiskLevel = CleanerRiskLevel.Safe,
                                        Description = $"{count} files, {size / (1024.0 * 1024):F1} MB"
                                    });
                                }
                            }
                        }
                    }
                    else
                    {
                        var (size, count) = GetDirectorySize(browser.CachePath);
                        if (size > 0)
                        {
                            results.Add(new CleanerScanResult
                            {
                                Category = CleanerCategory.BrowserCache,
                                Name = $"{browser.Name} Cache",
                                Path = browser.CachePath,
                                SizeBytes = size,
                                FileCount = count,
                                IsSelected = true,
                                RiskLevel = CleanerRiskLevel.Safe,
                                Description = $"{count} files, {size / (1024.0 * 1024):F1} MB"
                            });
                        }
                    }
                }
                catch { }
            }
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
