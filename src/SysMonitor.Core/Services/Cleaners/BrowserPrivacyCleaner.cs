using Microsoft.Extensions.Logging;
using SysMonitor.Core.Helpers;
using SysMonitor.Core.Services.Utilities;

namespace SysMonitor.Core.Services.Cleaners;

public interface IBrowserPrivacyCleaner
{
    Task<List<BrowserPrivacyItem>> ScanAsync(CancellationToken cancellationToken = default);
    Task<PrivacyCleanResult> CleanAsync(IEnumerable<BrowserPrivacyItem> itemsToClean, CancellationToken cancellationToken = default);
}

public class BrowserPrivacyItem
{
    public string BrowserName { get; set; } = string.Empty;
    public string BrowserIcon { get; set; } = string.Empty;
    public PrivacyDataType DataType { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string FormattedSize { get; set; } = string.Empty;
    public int ItemCount { get; set; }
    public bool IsSelected { get; set; }
    public PrivacyRiskLevel RiskLevel { get; set; }
}

public enum PrivacyDataType
{
    BrowsingHistory,
    Cookies,
    Cache,
    SavedPasswords,
    AutofillData,
    DownloadHistory,
    SessionData,
    LocalStorage,
    IndexedDB,
    ServiceWorkers
}

public enum PrivacyRiskLevel
{
    Safe,       // Cache, temp data
    Low,        // History, downloads
    Medium,     // Cookies, sessions
    High        // Passwords, autofill
}

public class PrivacyCleanResult
{
    public bool Success { get; set; }
    public long BytesCleaned { get; set; }
    public int ItemsDeleted { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public Dictionary<string, int> CleanedByBrowser { get; set; } = new();
}

public class BrowserPrivacyCleaner : IBrowserPrivacyCleaner
{
    private readonly record struct BrowserProfile(string Name, string BasePath, string Icon);

    private readonly ILogger<BrowserPrivacyCleaner> _logger;
    private readonly List<BrowserProfile> _browsers;

    public BrowserPrivacyCleaner(ILogger<BrowserPrivacyCleaner> logger)
    {
        _logger = logger;
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        _browsers = new List<BrowserProfile>
        {
            new("Google Chrome", Path.Combine(localAppData, "Google", "Chrome", "User Data"), "\uE774"),
            new("Microsoft Edge", Path.Combine(localAppData, "Microsoft", "Edge", "User Data"), "\uE774"),
            new("Mozilla Firefox", Path.Combine(roamingAppData, "Mozilla", "Firefox", "Profiles"), "\uEB41"),
            new("Brave", Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data"), "\uE8A7"),
            new("Opera", Path.Combine(roamingAppData, "Opera Software", "Opera Stable"), "\uE774"),
            new("Vivaldi", Path.Combine(localAppData, "Vivaldi", "User Data"), "\uE774")
        };
    }

    public async Task<List<BrowserPrivacyItem>> ScanAsync(CancellationToken cancellationToken = default)
    {
        var items = new List<BrowserPrivacyItem>();

        await Task.Run(() =>
        {
            foreach (var browser in _browsers)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(browser.BasePath)) continue;

                try
                {
                    if (browser.Name == "Mozilla Firefox")
                    {
                        ScanFirefox(browser, items);
                    }
                    else
                    {
                        ScanChromiumBrowser(browser, items);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to scan browser {Browser}", browser.Name);
                }
            }
        }, cancellationToken);

        _logger.LogDebug("Found {Count} browser privacy items", items.Count);
        return items.OrderBy(i => i.BrowserName).ThenBy(i => i.DataType).ToList();
    }

    private void ScanChromiumBrowser(BrowserProfile browser, List<BrowserPrivacyItem> items)
    {
        var defaultProfile = Path.Combine(browser.BasePath, "Default");
        if (!Directory.Exists(defaultProfile)) return;

        // Cookies
        var cookiesPath = Path.Combine(defaultProfile, "Network", "Cookies");
        if (File.Exists(cookiesPath))
        {
            var size = new FileInfo(cookiesPath).Length;
            items.Add(new BrowserPrivacyItem
            {
                BrowserName = browser.Name,
                BrowserIcon = browser.Icon,
                DataType = PrivacyDataType.Cookies,
                DisplayName = "Cookies",
                Description = "Website cookies and tracking data",
                Path = cookiesPath,
                SizeBytes = size,
                FormattedSize = FormatHelper.FormatSize(size),
                IsSelected = false,
                RiskLevel = PrivacyRiskLevel.Medium
            });
        }

        // History
        var historyPath = Path.Combine(defaultProfile, "History");
        if (File.Exists(historyPath))
        {
            var size = new FileInfo(historyPath).Length;
            items.Add(new BrowserPrivacyItem
            {
                BrowserName = browser.Name,
                BrowserIcon = browser.Icon,
                DataType = PrivacyDataType.BrowsingHistory,
                DisplayName = "Browsing History",
                Description = "Visited websites and search history",
                Path = historyPath,
                SizeBytes = size,
                FormattedSize = FormatHelper.FormatSize(size),
                IsSelected = false,
                RiskLevel = PrivacyRiskLevel.Low
            });
        }

        // Cache
        var cachePath = Path.Combine(defaultProfile, "Cache", "Cache_Data");
        if (Directory.Exists(cachePath))
        {
            var (size, count) = GetDirectorySize(cachePath);
            items.Add(new BrowserPrivacyItem
            {
                BrowserName = browser.Name,
                BrowserIcon = browser.Icon,
                DataType = PrivacyDataType.Cache,
                DisplayName = "Browser Cache",
                Description = $"{count} cached files",
                Path = cachePath,
                SizeBytes = size,
                FormattedSize = FormatHelper.FormatSize(size),
                ItemCount = count,
                IsSelected = true,
                RiskLevel = PrivacyRiskLevel.Safe
            });
        }

        // Login Data (Saved Passwords)
        var loginDataPath = Path.Combine(defaultProfile, "Login Data");
        if (File.Exists(loginDataPath))
        {
            var size = new FileInfo(loginDataPath).Length;
            items.Add(new BrowserPrivacyItem
            {
                BrowserName = browser.Name,
                BrowserIcon = browser.Icon,
                DataType = PrivacyDataType.SavedPasswords,
                DisplayName = "Saved Passwords",
                Description = "Stored login credentials (WARNING: Cannot be recovered!)",
                Path = loginDataPath,
                SizeBytes = size,
                FormattedSize = FormatHelper.FormatSize(size),
                IsSelected = false,
                RiskLevel = PrivacyRiskLevel.High
            });
        }

        // Autofill Data
        var autofillPath = Path.Combine(defaultProfile, "Web Data");
        if (File.Exists(autofillPath))
        {
            var size = new FileInfo(autofillPath).Length;
            items.Add(new BrowserPrivacyItem
            {
                BrowserName = browser.Name,
                BrowserIcon = browser.Icon,
                DataType = PrivacyDataType.AutofillData,
                DisplayName = "Autofill Data",
                Description = "Saved form data, addresses, payment methods",
                Path = autofillPath,
                SizeBytes = size,
                FormattedSize = FormatHelper.FormatSize(size),
                IsSelected = false,
                RiskLevel = PrivacyRiskLevel.High
            });
        }

        // Download History (part of History file, but we can note it)
        // Session Data
        var sessionPath = Path.Combine(defaultProfile, "Sessions");
        if (Directory.Exists(sessionPath))
        {
            var (size, count) = GetDirectorySize(sessionPath);
            items.Add(new BrowserPrivacyItem
            {
                BrowserName = browser.Name,
                BrowserIcon = browser.Icon,
                DataType = PrivacyDataType.SessionData,
                DisplayName = "Session Data",
                Description = "Open tabs and windows data",
                Path = sessionPath,
                SizeBytes = size,
                FormattedSize = FormatHelper.FormatSize(size),
                ItemCount = count,
                IsSelected = false,
                RiskLevel = PrivacyRiskLevel.Low
            });
        }

        // Local Storage
        var localStoragePath = Path.Combine(defaultProfile, "Local Storage", "leveldb");
        if (Directory.Exists(localStoragePath))
        {
            var (size, count) = GetDirectorySize(localStoragePath);
            items.Add(new BrowserPrivacyItem
            {
                BrowserName = browser.Name,
                BrowserIcon = browser.Icon,
                DataType = PrivacyDataType.LocalStorage,
                DisplayName = "Local Storage",
                Description = "Website local data storage",
                Path = localStoragePath,
                SizeBytes = size,
                FormattedSize = FormatHelper.FormatSize(size),
                ItemCount = count,
                IsSelected = false,
                RiskLevel = PrivacyRiskLevel.Medium
            });
        }

        // Service Workers
        var swPath = Path.Combine(defaultProfile, "Service Worker", "CacheStorage");
        if (Directory.Exists(swPath))
        {
            var (size, count) = GetDirectorySize(swPath);
            items.Add(new BrowserPrivacyItem
            {
                BrowserName = browser.Name,
                BrowserIcon = browser.Icon,
                DataType = PrivacyDataType.ServiceWorkers,
                DisplayName = "Service Workers",
                Description = "Offline website data and push notifications",
                Path = swPath,
                SizeBytes = size,
                FormattedSize = FormatHelper.FormatSize(size),
                ItemCount = count,
                IsSelected = true,
                RiskLevel = PrivacyRiskLevel.Safe
            });
        }
    }

    private void ScanFirefox(BrowserProfile browser, List<BrowserPrivacyItem> items)
    {
        if (!Directory.Exists(browser.BasePath)) return;

        // Firefox keeps its profiles in Roaming and each profile's cache in a folder of the same name under Local.
        // The profile is the one in use: of the .default ones, the one whose history was written to last - an
        // old, empty xxxx.default often sits beside the xxxx.default-release Firefox actually opens.
        var localProfiles = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mozilla", "Firefox", "Profiles");
        var inUse = Directory.GetDirectories(browser.BasePath)
            .Where(dir => Path.GetFileName(dir).Contains(".default", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(dir => File.Exists(Path.Combine(dir, "places.sqlite"))
                ? File.GetLastWriteTimeUtc(Path.Combine(dir, "places.sqlite"))
                : DateTime.MinValue)
            .Take(1);

        foreach (var profileDir in inUse)
        {

            // Cookies
            var cookiesPath = Path.Combine(profileDir, "cookies.sqlite");
            if (File.Exists(cookiesPath))
            {
                var size = new FileInfo(cookiesPath).Length;
                items.Add(new BrowserPrivacyItem
                {
                    BrowserName = browser.Name,
                    BrowserIcon = browser.Icon,
                    DataType = PrivacyDataType.Cookies,
                    DisplayName = "Cookies",
                    Description = "Website cookies and tracking data",
                    Path = cookiesPath,
                    SizeBytes = size,
                    FormattedSize = FormatHelper.FormatSize(size),
                    IsSelected = false,
                    RiskLevel = PrivacyRiskLevel.Medium
                });
            }

            // History
            var historyPath = Path.Combine(profileDir, "places.sqlite");
            if (File.Exists(historyPath))
            {
                var size = new FileInfo(historyPath).Length;
                items.Add(new BrowserPrivacyItem
                {
                    BrowserName = browser.Name,
                    BrowserIcon = browser.Icon,
                    DataType = PrivacyDataType.BrowsingHistory,
                    // Firefox keeps bookmarks in the same database as history, so clearing one clears both.
                    DisplayName = "History and Bookmarks",
                    Description = "Visited websites and search history - and every bookmark (WARNING: Firefox keeps them in the same file)",
                    Path = historyPath,
                    SizeBytes = size,
                    FormattedSize = FormatHelper.FormatSize(size),
                    IsSelected = false,
                    RiskLevel = PrivacyRiskLevel.High
                });
            }

            // Cache
            var cachePath = Path.Combine(localProfiles, Path.GetFileName(profileDir), "cache2");
            if (Directory.Exists(cachePath))
            {
                var (size, count) = GetDirectorySize(cachePath);
                items.Add(new BrowserPrivacyItem
                {
                    BrowserName = browser.Name,
                    BrowserIcon = browser.Icon,
                    DataType = PrivacyDataType.Cache,
                    DisplayName = "Browser Cache",
                    Description = $"{count} cached files",
                    Path = cachePath,
                    SizeBytes = size,
                    FormattedSize = FormatHelper.FormatSize(size),
                    ItemCount = count,
                    IsSelected = true,
                    RiskLevel = PrivacyRiskLevel.Safe
                });
            }

            // Saved Passwords
            var loginsPath = Path.Combine(profileDir, "logins.json");
            if (File.Exists(loginsPath))
            {
                var size = new FileInfo(loginsPath).Length;
                items.Add(new BrowserPrivacyItem
                {
                    BrowserName = browser.Name,
                    BrowserIcon = browser.Icon,
                    DataType = PrivacyDataType.SavedPasswords,
                    DisplayName = "Saved Passwords",
                    Description = "Stored login credentials (WARNING: Cannot be recovered!)",
                    Path = loginsPath,
                    SizeBytes = size,
                    FormattedSize = FormatHelper.FormatSize(size),
                    IsSelected = false,
                    RiskLevel = PrivacyRiskLevel.High
                });
            }

            // Form History (Autofill)
            var formHistoryPath = Path.Combine(profileDir, "formhistory.sqlite");
            if (File.Exists(formHistoryPath))
            {
                var size = new FileInfo(formHistoryPath).Length;
                items.Add(new BrowserPrivacyItem
                {
                    BrowserName = browser.Name,
                    BrowserIcon = browser.Icon,
                    DataType = PrivacyDataType.AutofillData,
                    DisplayName = "Form History",
                    Description = "Saved form entries and autofill data",
                    Path = formHistoryPath,
                    SizeBytes = size,
                    FormattedSize = FormatHelper.FormatSize(size),
                    IsSelected = false,
                    RiskLevel = PrivacyRiskLevel.Medium
                });
            }
        }
    }

    public async Task<PrivacyCleanResult> CleanAsync(IEnumerable<BrowserPrivacyItem> itemsToClean,
        CancellationToken cancellationToken = default)
    {
        var result = new PrivacyCleanResult { Success = true };
        var startTime = DateTime.Now;

        await Task.Run(() =>
        {
            foreach (var item in itemsToClean.Where(i => i.IsSelected))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (File.Exists(item.Path))
                    {
                        var size = new FileInfo(item.Path).Length;
                        File.Delete(item.Path);
                        result.BytesCleaned += size;
                        result.ItemsDeleted++;

                        // A database's journal and write-ahead log hold the same rows; left behind, the browser
                        // replays them into a fresh file on its next start, or finds them torn from their database.
                        foreach (var sidecar in new[] { "-journal", "-wal", "-shm" }.Select(suffix => item.Path + suffix))
                        {
                            if (File.Exists(sidecar))
                            {
                                result.BytesCleaned += new FileInfo(sidecar).Length;
                                File.Delete(sidecar);
                            }
                        }
                    }
                    else if (Directory.Exists(item.Path))
                    {
                        // Freed is counted file by file, from what was actually deleted: a running browser holds
                        // part of its cache open, and that part is still on the disk.
                        foreach (var file in FileScanning.EnumerateFiles(item.Path, cancellationToken))
                        {
                            try
                            {
                                var size = new FileInfo(file).Length;
                                File.Delete(file);
                                result.BytesCleaned += size;
                                result.ItemsDeleted++;
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                result.ErrorCount++;
                                _logger.LogTrace(ex, "Failed to delete file during privacy clean");
                            }
                        }
                    }

                    // Track by browser
                    if (!result.CleanedByBrowser.ContainsKey(item.BrowserName))
                        result.CleanedByBrowser[item.BrowserName] = 0;
                    result.CleanedByBrowser[item.BrowserName]++;
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    result.Errors.Add($"{item.BrowserName} - {item.DisplayName}: {ex.Message}");
                    _logger.LogWarning(ex, "Failed to clean {Browser} - {Item}", item.BrowserName, item.DisplayName);
                }
            }
        }, cancellationToken);

        result.Duration = DateTime.Now - startTime;
        // Files a running browser holds open are left, not failures; see TempFileCleaner.CleanAsync.
        result.Success = result.Errors.Count == 0 && !(result.ItemsDeleted == 0 && result.ErrorCount > 0);
        _logger.LogInformation("Privacy clean complete: {ItemsDeleted} items deleted, {BytesCleaned} bytes freed",
            result.ItemsDeleted, result.BytesCleaned);
        return result;
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
                    // Best effort: a file the browser still holds open is left where it is; the clean
                    // carries on with the rest.
                }
            }
        }
        catch
        {
            // Best effort: a folder this account cannot read is skipped, not failed on.
        }

        return (size, count);
    }
}
