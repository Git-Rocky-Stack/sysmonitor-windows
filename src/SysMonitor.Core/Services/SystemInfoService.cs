using System.Management;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Monitors;

namespace SysMonitor.Core.Services;

public class SystemInfoService : ISystemInfoService
{
    private readonly ICpuMonitor _cpuMonitor;
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly IDiskMonitor _diskMonitor;
    private readonly IBatteryMonitor _batteryMonitor;
    private readonly INetworkMonitor _networkMonitor;

    private OsInfo? _cachedOsInfo;
    private DateTime? _bootTime;

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
    }

    public async Task<SystemInfo> GetSystemInfoAsync()
    {
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

    private int CalculateHealthScore(SystemInfo info)
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

        // Disk space penalty
        foreach (var disk in info.Disks)
        {
            if (disk.UsagePercent > 95) score -= 10;
            else if (disk.UsagePercent > 85) score -= 5;
            else if (disk.UsagePercent > 75) score -= 2;
        }

        // Battery health penalty
        if (info.Battery != null && !info.Battery.IsPluggedIn && info.Battery.ChargePercent < 20)
            score -= 5;

        return Math.Max(0, Math.Min(100, score));
    }

    public async Task<CpuInfo> GetCpuInfoAsync() => await _cpuMonitor.GetCpuInfoAsync();
    public async Task<MemoryInfo> GetMemoryInfoAsync() => await _memoryMonitor.GetMemoryInfoAsync();
    public async Task<List<DiskInfo>> GetDiskInfoAsync() => await _diskMonitor.GetAllDisksAsync();
    public async Task<BatteryInfo?> GetBatteryInfoAsync() => await _batteryMonitor.GetBatteryInfoAsync();
    public async Task<NetworkInfo> GetNetworkInfoAsync() => await _networkMonitor.GetNetworkInfoAsync();

    public async Task<OsInfo> GetOsInfoAsync()
    {
        // Return cached info with updated uptime
        if (_cachedOsInfo != null && _bootTime.HasValue)
        {
            _cachedOsInfo.Uptime = DateTime.Now - _bootTime.Value;
            return _cachedOsInfo;
        }

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
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem");
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
                        _bootTime = ManagementDateTimeConverter.ToDateTime(obj["LastBootUpTime"].ToString()!);
                        info.Uptime = DateTime.Now - _bootTime.Value;
                    }
                    break;
                }
            }
            catch { }

            _cachedOsInfo = info;
            return info;
        });
    }
}
