using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services;

/// <summary>
/// Central service for collecting system information.
/// </summary>
public interface ISystemInfoService
{
    Task<SystemInfo> GetSystemInfoAsync();
    Task<int> GetHealthScoreAsync();
    Task<CpuInfo> GetCpuInfoAsync();
    Task<MemoryInfo> GetMemoryInfoAsync();
    Task<List<DiskInfo>> GetDiskInfoAsync();
    Task<BatteryInfo?> GetBatteryInfoAsync();
    Task<NetworkInfo> GetNetworkInfoAsync();
    Task<OsInfo> GetOsInfoAsync();
}
