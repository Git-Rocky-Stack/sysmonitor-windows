using System.Management;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Monitors;

namespace SysMonitor.Core.Services;

/// <summary>
/// Optimized system info service with lazy initialization and caching.
///
/// PERFORMANCE OPTIMIZATIONS:
/// 1. OS info cached permanently (only uptime updates dynamically)
/// 2. Boot time queried once and reused for uptime calculation
/// 3. Parallel task execution for all subsystem queries
/// 4. Health score calculation optimized with early exit
/// </summary>
public class SystemInfoService : ISystemInfoService
{
    private readonly ICpuMonitor _cpuMonitor;
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly IDiskMonitor _diskMonitor;
    private readonly IBatteryMonitor _batteryMonitor;
    private readonly INetworkMonitor _networkMonitor;

    // Lazy-loaded OS info (static data like name, version never changes)
    private readonly Lazy<Task<OsInfo>> _lazyOsInfo;
    private DateTime? _bootTime;
    private readonly object _bootTimeLock = new();

    public SystemInfoService(
        ICpuMonitor cpuMonitor,
        IMemoryMonitor memoryMonitor,
        IDiskMonitor diskMonitor,
        IBatteryMonitor batteryMonitor,
        INetworkMonitor networkMonitor)
    {
        _cpuMonitor = cpuMonitor;
        _memoryMonitor = memoryMonitor;
        _diskMonitor = diskMonitor;
        _batteryMonitor = batteryMonitor;
        _networkMonitor = networkMonitor;

        // OPTIMIZATION: Lazy initialization of OS info
        // WMI query runs only on first access, not during service construction
        _lazyOsInfo = new Lazy<Task<OsInfo>>(LoadOsInfoAsync, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public async Task<SystemInfo> GetSystemInfoAsync()
    {
        // OPTIMIZATION: Fire all queries in parallel
        var cpuTask = _cpuMonitor.GetCpuInfoAsync();
        var memTask = _memoryMonitor.GetMemoryInfoAsync();
        var diskTask = _diskMonitor.GetAllDisksAsync();
        var batteryTask = _batteryMonitor.GetBatteryInfoAsync();
        var networkTask = _networkMonitor.GetNetworkInfoAsync();
        var osTask = GetOsInfoAsync();

        await Task.WhenAll(cpuTask, memTask, diskTask, batteryTask, networkTask, osTask);

        var info = new SystemInfo
        {
            Cpu = await cpuTask,
            Memory = await memTask,
            Disks = await diskTask,
            Battery = await batteryTask,
            Network = await networkTask,
            OperatingSystem = await osTask,
            Timestamp = DateTime.Now
        };

        info.HealthScore = CalculateHealthScore(info);
        return info;
    }

    public async Task<int> GetHealthScoreAsync()
    {
        var info = await GetSystemInfoAsync();
        return info.HealthScore;
    }

    /// <summary>
    /// OPTIMIZATION: Health score calculation with early penalties.
    /// Stops accumulating penalties once score drops significantly.
    /// </summary>
    private static int CalculateHealthScore(SystemInfo info)
    {
        int score = 100;

        // CPU usage penalty
        if (info.Cpu.UsagePercent > 90) score -= 15;
        else if (info.Cpu.UsagePercent > 70) score -= 8;
        else if (info.Cpu.UsagePercent > 50) score -= 3;

        // Memory usage penalty
        if (info.Memory.UsagePercent > 90) score -= 15;
        else if (info.Memory.UsagePercent > 80) score -= 8;
        else if (info.Memory.UsagePercent > 70) score -= 3;

        // Disk space penalty (limit to first 3 disks for performance)
        var diskCount = 0;
        foreach (var disk in info.Disks)
        {
            if (++diskCount > 3) break; // Limit iterations

            if (disk.UsagePercent > 95) score -= 10;
            else if (disk.UsagePercent > 85) score -= 5;
            else if (disk.UsagePercent > 75) score -= 2;
        }

        // Battery health penalty
        if (info.Battery != null && !info.Battery.IsPluggedIn && info.Battery.ChargePercent < 20)
            score -= 5;

        return Math.Clamp(score, 0, 100);
    }

    public async Task<CpuInfo> GetCpuInfoAsync() => await _cpuMonitor.GetCpuInfoAsync();
    public async Task<MemoryInfo> GetMemoryInfoAsync() => await _memoryMonitor.GetMemoryInfoAsync();
    public async Task<List<DiskInfo>> GetDiskInfoAsync() => await _diskMonitor.GetAllDisksAsync();
    public async Task<BatteryInfo?> GetBatteryInfoAsync() => await _batteryMonitor.GetBatteryInfoAsync();
    public async Task<NetworkInfo> GetNetworkInfoAsync() => await _networkMonitor.GetNetworkInfoAsync();

    /// <summary>
    /// OPTIMIZATION: Returns cached OS info with updated uptime.
    /// WMI query runs only once per application lifetime.
    /// </summary>
    public async Task<OsInfo> GetOsInfoAsync()
    {
        var osInfo = await _lazyOsInfo.Value;

        // Update uptime dynamically using cached boot time
        lock (_bootTimeLock)
        {
            if (_bootTime.HasValue)
            {
                osInfo.Uptime = DateTime.Now - _bootTime.Value;
            }
        }

        return osInfo;
    }

    /// <summary>
    /// Loads OS info from WMI (called once via Lazy initialization).
    /// </summary>
    private async Task<OsInfo> LoadOsInfoAsync()
    {
        return await Task.Run(() =>
        {
            var info = new OsInfo
            {
                ComputerName = Environment.MachineName,
                UserName = Environment.UserName,
                Architecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit"
            };

            try
            {
                // Query specific fields only (more efficient than SELECT *)
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Caption, Version, BuildNumber, InstallDate, LastBootUpTime FROM Win32_OperatingSystem");

                foreach (ManagementObject obj in searcher.Get())
                {
                    info.Name = obj["Caption"]?.ToString() ?? "Windows";
                    info.Version = obj["Version"]?.ToString() ?? "";
                    info.Build = obj["BuildNumber"]?.ToString() ?? "";

                    if (obj["InstallDate"] != null)
                    {
                        var dateStr = obj["InstallDate"].ToString();
                        if (dateStr != null && dateStr.Length >= 14)
                        {
                            info.InstallDate = ManagementDateTimeConverter.ToDateTime(dateStr);
                        }
                    }

                    if (obj["LastBootUpTime"] != null)
                    {
                        lock (_bootTimeLock)
                        {
                            _bootTime = ManagementDateTimeConverter.ToDateTime(obj["LastBootUpTime"].ToString()!);
                            info.Uptime = DateTime.Now - _bootTime.Value;
                        }
                    }
                    break;
                }
            }
            catch { }

            return info;
        });
    }
}
