using System.Text.Json;

namespace SysMonitor.Core.Services.GameMode;

/// <summary>
/// Service for managing a RAM-based temp cache by redirecting TEMP/TMP paths.
/// </summary>
public class RamCacheService : IRamCacheService
{
    private readonly string _cachePath;
    private readonly string _settingsPath;
    private string? _originalTempPath;
    private string? _originalTmpPath;
    private long _allocatedSizeBytes;

    public bool IsEnabled { get; private set; }
    public long AllocatedSizeBytes => _allocatedSizeBytes;
    public long UsedSizeBytes => CalculateUsedSize();
    public string CachePath => _cachePath;

    public event EventHandler<RamCacheStats>? StatsUpdated;

    public RamCacheService()
    {
        _cachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "RamCache");

        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "ramcache-settings.json");

        // Restore state from settings
        LoadSettings();
    }

    private void LoadSettings()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<RamCacheSettings>(json);
                if (settings != null)
                {
                    _originalTempPath = settings.OriginalTempPath;
                    _originalTmpPath = settings.OriginalTmpPath;
                    _allocatedSizeBytes = settings.AllocatedSizeBytes;
                    IsEnabled = settings.IsEnabled;

                    // Verify cache is still active (TEMP still points to our path)
                    var currentTemp = Environment.GetEnvironmentVariable("TEMP");
                    if (currentTemp != _cachePath)
                    {
                        // Cache was reset externally
                        IsEnabled = false;
                        SaveSettings();
                    }
                }
            }
        }
        catch
        {
            // Failed to load settings
        }
    }

    private void SaveSettings()
    {
        try
        {
            var settings = new RamCacheSettings
            {
                OriginalTempPath = _originalTempPath,
                OriginalTmpPath = _originalTmpPath,
                AllocatedSizeBytes = _allocatedSizeBytes,
                IsEnabled = IsEnabled
            };

            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // Failed to save settings
        }
    }

    public async Task<bool> EnableAsync(long sizeMegabytes)
    {
        if (IsEnabled) return true;

        try
        {
            // Validate size (512MB to 4GB)
            sizeMegabytes = Math.Max(512, Math.Min(4096, sizeMegabytes));
            _allocatedSizeBytes = sizeMegabytes * 1024 * 1024;

            // Store original paths
            _originalTempPath = Environment.GetEnvironmentVariable("TEMP");
            _originalTmpPath = Environment.GetEnvironmentVariable("TMP");

            // Create cache directory
            if (!Directory.Exists(_cachePath))
            {
                Directory.CreateDirectory(_cachePath);
            }

            // Clear any existing files
            await ClearCacheAsync();

            // Set environment variables for this process
            Environment.SetEnvironmentVariable("TEMP", _cachePath);
            Environment.SetEnvironmentVariable("TMP", _cachePath);

            IsEnabled = true;
            SaveSettings();

            RaiseStatsUpdated();

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task DisableAsync()
    {
        if (!IsEnabled) return;

        try
        {
            // Restore original paths
            if (!string.IsNullOrEmpty(_originalTempPath))
            {
                Environment.SetEnvironmentVariable("TEMP", _originalTempPath);
            }
            if (!string.IsNullOrEmpty(_originalTmpPath))
            {
                Environment.SetEnvironmentVariable("TMP", _originalTmpPath);
            }

            // Clear cache
            await ClearCacheAsync();

            IsEnabled = false;
            _allocatedSizeBytes = 0;
            SaveSettings();

            RaiseStatsUpdated();
        }
        catch
        {
            // Best effort
        }
    }

    public async Task<RamCacheStats> GetStatsAsync()
    {
        return await Task.FromResult(new RamCacheStats
        {
            IsEnabled = IsEnabled,
            AllocatedBytes = _allocatedSizeBytes,
            UsedBytes = UsedSizeBytes,
            FileCount = GetFileCount(),
            CachePath = _cachePath
        });
    }

    public async Task ClearCacheAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(_cachePath))
                {
                    var dir = new DirectoryInfo(_cachePath);
                    foreach (var file in dir.GetFiles())
                    {
                        try { file.Delete(); } catch { }
                    }
                    foreach (var subDir in dir.GetDirectories())
                    {
                        try { subDir.Delete(true); } catch { }
                    }
                }
            }
            catch
            {
                // Best effort
            }
        });

        RaiseStatsUpdated();
    }

    private long CalculateUsedSize()
    {
        try
        {
            if (!Directory.Exists(_cachePath))
                return 0;

            return new DirectoryInfo(_cachePath)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Sum(f => f.Length);
        }
        catch
        {
            return 0;
        }
    }

    private int GetFileCount()
    {
        try
        {
            if (!Directory.Exists(_cachePath))
                return 0;

            return new DirectoryInfo(_cachePath)
                .EnumerateFiles("*", SearchOption.AllDirectories)
                .Count();
        }
        catch
        {
            return 0;
        }
    }

    private void RaiseStatsUpdated()
    {
        StatsUpdated?.Invoke(this, new RamCacheStats
        {
            IsEnabled = IsEnabled,
            AllocatedBytes = _allocatedSizeBytes,
            UsedBytes = UsedSizeBytes,
            FileCount = GetFileCount(),
            CachePath = _cachePath
        });
    }

    private class RamCacheSettings
    {
        public string? OriginalTempPath { get; set; }
        public string? OriginalTmpPath { get; set; }
        public long AllocatedSizeBytes { get; set; }
        public bool IsEnabled { get; set; }
    }
}
