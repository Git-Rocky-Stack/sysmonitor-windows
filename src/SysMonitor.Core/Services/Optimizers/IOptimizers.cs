using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Optimizers;

public interface IStartupOptimizer
{
    Task<List<StartupItem>> GetStartupItemsAsync();
    Task<bool> EnableStartupItemAsync(StartupItem item);
    Task<bool> DisableStartupItemAsync(StartupItem item);
    Task<bool> DeleteStartupItemAsync(StartupItem item);
}

public interface IMemoryOptimizer
{
    Task<long> OptimizeMemoryAsync();
    Task<long> TrimProcessWorkingSetAsync(int processId);
    Task<long> ClearStandbyListAsync();
}

public interface IServiceOptimizer
{
    Task<List<ServiceInfo>> GetServicesAsync();
    Task<bool> StartServiceAsync(string serviceName);
    Task<bool> StopServiceAsync(string serviceName);
    Task<bool> SetStartModeAsync(string serviceName, ServiceStartMode mode);
    Task<List<ServiceInfo>> GetOptimizableServicesAsync();
}
