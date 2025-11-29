using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

public interface ICpuMonitor
{
    Task<CpuInfo> GetCpuInfoAsync();
    Task<double> GetUsagePercentAsync();
    Task<double> GetTemperatureAsync();
    Task<List<double>> GetCoreUsagesAsync();
}

public interface IMemoryMonitor
{
    Task<MemoryInfo> GetMemoryInfoAsync();
    Task<double> GetUsagePercentAsync();
}

public interface IDiskMonitor
{
    Task<List<DiskInfo>> GetAllDisksAsync();
    Task<DiskInfo?> GetDiskAsync(string driveLetter);
    Task<(double read, double write)> GetDiskSpeedAsync(string driveLetter);
}

public interface IBatteryMonitor
{
    Task<BatteryInfo?> GetBatteryInfoAsync();
    bool HasBattery { get; }
}

public interface INetworkMonitor
{
    Task<NetworkInfo> GetNetworkInfoAsync();
    Task<List<NetworkAdapter>> GetAdaptersAsync();
    Task<(double upload, double download)> GetSpeedAsync();
}

public interface IProcessMonitor
{
    Task<List<ProcessInfo>> GetAllProcessesAsync();
    Task<ProcessInfo?> GetProcessAsync(int processId);
    Task<bool> KillProcessAsync(int processId);
    Task<bool> SetPriorityAsync(int processId, ProcessPriority priority);
}

public interface ITemperatureMonitor
{
    Task InitializeAsync();
    Task<Dictionary<string, double>> GetAllTemperaturesAsync();
    Task<double> GetCpuTemperatureAsync();
    Task<double> GetGpuTemperatureAsync();
    void Dispose();
}
