using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Optimizers;

public interface IStartupOptimizer
{
    Task<List<StartupItem>> GetStartupItemsAsync();

    /// <summary>Lets an item start with Windows again, and says what happened.</summary>
    Task<StartupChangeResult> EnableStartupItemAsync(StartupItem item);

    /// <summary>Stops an item starting with Windows, reversibly, and says what happened.</summary>
    Task<StartupChangeResult> DisableStartupItemAsync(StartupItem item);

    /// <summary>Removes an item from startup altogether, and says what happened.</summary>
    Task<StartupChangeResult> DeleteStartupItemAsync(StartupItem item);
}

public interface IMemoryOptimizer
{
    Task<long> OptimizeMemoryAsync();
    Task<long> TrimProcessWorkingSetAsync(int processId);
}

public interface IServiceOptimizer
{
    Task<List<ServiceInfo>> GetServicesAsync();
    Task<bool> StartServiceAsync(string serviceName);
    Task<bool> StopServiceAsync(string serviceName);
    Task<bool> SetStartModeAsync(string serviceName, ServiceStartMode mode);
    Task<List<ServiceInfo>> GetOptimizableServicesAsync();
}
