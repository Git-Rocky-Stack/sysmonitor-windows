using System.Collections.Concurrent;
using System.Management;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

/// <summary>
/// Optimized disk monitor with WMI query caching.
///
/// PERFORMANCE OPTIMIZATIONS:
/// 1. Caches SSD detection results (disk type doesn't change at runtime)
/// 2. Uses concurrent dictionary for thread-safe cache access
/// 3. Separates static disk info from dynamic space info
/// 4. Lazy WMI query execution only when needed
/// </summary>
public class DiskMonitor : IDiskMonitor
{
    // Cache for SSD detection results (drive type never changes at runtime)
    private static readonly ConcurrentDictionary<string, bool> SsdCache = new(StringComparer.OrdinalIgnoreCase);

    // Cache for all physical disk media types (queried once)
    private static readonly Lazy<Dictionary<string, bool>> PhysicalDiskCache = new(
        () => LoadPhysicalDiskInfo(),
        LazyThreadSafetyMode.ExecutionAndPublication);

    public async Task<List<DiskInfo>> GetAllDisksAsync()
    {
        return await Task.Run(() =>
        {
            var disks = new List<DiskInfo>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    disks.Add(new DiskInfo
                    {
                        Name = drive.Name,
                        Label = drive.VolumeLabel,
                        DriveType = drive.DriveType.ToString(),
                        FileSystem = drive.DriveFormat,
                        TotalBytes = drive.TotalSize,
                        FreeBytes = drive.AvailableFreeSpace,
                        UsedBytes = drive.TotalSize - drive.AvailableFreeSpace,
                        UsagePercent = (1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100,
                        IsSSD = IsSSDCached(drive.Name)
                    });
                }
                catch { }
            }
            return disks;
        });
    }

    public async Task<DiskInfo?> GetDiskAsync(string driveLetter)
    {
        var disks = await GetAllDisksAsync();
        return disks.FirstOrDefault(d => d.Name.StartsWith(driveLetter, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<(double read, double write)> GetDiskSpeedAsync(string driveLetter)
    {
        return await Task.FromResult((0.0, 0.0));
    }

    /// <summary>
    /// OPTIMIZATION: Cached SSD detection using pre-loaded physical disk info.
    /// WMI query runs only once per application lifetime.
    /// </summary>
    private static bool IsSSDCached(string driveLetter)
    {
        var normalizedLetter = driveLetter.TrimEnd(':', '\\').ToUpperInvariant();

        // Check instance cache first
        if (SsdCache.TryGetValue(normalizedLetter, out var cachedResult))
        {
            return cachedResult;
        }

        // Check physical disk cache
        var physicalDisks = PhysicalDiskCache.Value;
        if (physicalDisks.TryGetValue(normalizedLetter, out var isSsd))
        {
            SsdCache[normalizedLetter] = isSsd;
            return isSsd;
        }

        // Fallback: assume not SSD if we can't determine
        SsdCache[normalizedLetter] = false;
        return false;
    }

    /// <summary>
    /// OPTIMIZATION: Loads all physical disk info in a single WMI query.
    /// This is more efficient than querying for each drive separately.
    /// </summary>
    private static Dictionary<string, bool> LoadPhysicalDiskInfo()
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Query all physical disks and their partitions in one go
            using var diskSearcher = new ManagementObjectSearcher(
                @"\\.\root\Microsoft\Windows\Storage",
                "SELECT DeviceId, MediaType FROM MSFT_PhysicalDisk");

            var diskMediaTypes = new Dictionary<string, int>();
            foreach (ManagementObject disk in diskSearcher.Get())
            {
                var deviceId = disk["DeviceId"]?.ToString();
                var mediaType = Convert.ToInt32(disk["MediaType"] ?? 0);
                if (deviceId != null)
                {
                    diskMediaTypes[deviceId] = mediaType;
                }
            }

            // Query partitions to map drive letters to physical disks
            using var partitionSearcher = new ManagementObjectSearcher(
                @"\\.\root\Microsoft\Windows\Storage",
                "SELECT DiskNumber, DriveLetter FROM MSFT_Partition WHERE DriveLetter IS NOT NULL");

            foreach (ManagementObject partition in partitionSearcher.Get())
            {
                var driveLetter = partition["DriveLetter"]?.ToString();
                var diskNumber = partition["DiskNumber"]?.ToString();

                if (!string.IsNullOrEmpty(driveLetter) && !string.IsNullOrEmpty(diskNumber))
                {
                    // MediaType 4 = SSD, 3 = HDD
                    var isSsd = diskMediaTypes.TryGetValue(diskNumber, out var mediaType) && mediaType == 4;
                    result[driveLetter] = isSsd;
                }
            }
        }
        catch
        {
            // WMI query failed - return empty dictionary
        }

        return result;
    }
}
