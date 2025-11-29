using System.Diagnostics;
using System.Management;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

public class CpuMonitor : ICpuMonitor, IDisposable
{
    private PerformanceCounter? _cpuCounter;
    private readonly List<PerformanceCounter> _coreCounters = new();
    private CpuInfo? _cachedStaticInfo;
    private DateTime _lastCacheTime = DateTime.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(30);
    private bool _isDisposed;

    public CpuMonitor()
    {
        try
        {
            _cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuCounter.NextValue();

            int processorCount = Environment.ProcessorCount;
            for (int i = 0; i < processorCount; i++)
            {
                var counter = new PerformanceCounter("Processor", "% Processor Time", i.ToString());
                counter.NextValue();
                _coreCounters.Add(counter);
            }
        }
        catch { }
    }

    public async Task<CpuInfo> GetCpuInfoAsync()
    {
        return await Task.Run(() =>
        {
            if (_cachedStaticInfo != null && DateTime.Now - _lastCacheTime < _cacheDuration)
            {
                _cachedStaticInfo.UsagePercent = GetCurrentUsage();
                _cachedStaticInfo.CoreUsages = GetCurrentCoreUsages();
                return _cachedStaticInfo;
            }

            var info = new CpuInfo();
            try
            {
                using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Processor");
                foreach (ManagementObject obj in searcher.Get())
                {
                    info.Name = obj["Name"]?.ToString()?.Trim() ?? "Unknown";
                    info.Manufacturer = obj["Manufacturer"]?.ToString() ?? "Unknown";
                    info.Cores = Convert.ToInt32(obj["NumberOfCores"] ?? 0);
                    info.LogicalProcessors = Convert.ToInt32(obj["NumberOfLogicalProcessors"] ?? Environment.ProcessorCount);
                    info.MaxClockSpeedMHz = Convert.ToDouble(obj["MaxClockSpeed"] ?? 0);
                    info.CurrentClockSpeedMHz = Convert.ToDouble(obj["CurrentClockSpeed"] ?? 0);
                    info.Architecture = GetArchitecture(Convert.ToInt32(obj["Architecture"] ?? 0));
                    info.CacheL2 = $"{Convert.ToInt32(obj["L2CacheSize"] ?? 0)} KB";
                    info.CacheL3 = $"{Convert.ToInt32(obj["L3CacheSize"] ?? 0)} KB";
                    break;
                }
            }
            catch { }

            info.UsagePercent = GetCurrentUsage();
            info.CoreUsages = GetCurrentCoreUsages();
            _cachedStaticInfo = info;
            _lastCacheTime = DateTime.Now;
            return info;
        });
    }

    private double GetCurrentUsage()
    {
        try { return _cpuCounter?.NextValue() ?? 0; }
        catch { return 0; }
    }

    private List<double> GetCurrentCoreUsages()
    {
        var usages = new List<double>();
        foreach (var counter in _coreCounters)
        {
            try { usages.Add(counter.NextValue()); }
            catch { usages.Add(0); }
        }
        return usages;
    }

    public async Task<double> GetUsagePercentAsync()
    {
        return await Task.Run(() => GetCurrentUsage());
    }

    public async Task<double> GetTemperatureAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM MSAcpi_ThermalZoneTemperature");
                foreach (ManagementObject obj in searcher.Get())
                {
                    double temp = Convert.ToDouble(obj["CurrentTemperature"]);
                    return (temp - 2732) / 10.0;
                }
            }
            catch { }
            return 0;
        });
    }

    public async Task<List<double>> GetCoreUsagesAsync()
    {
        return await Task.Run(() => GetCurrentCoreUsages());
    }

    private static string GetArchitecture(int arch) => arch switch
    {
        0 => "x86",
        9 => "x64",
        12 => "ARM64",
        _ => "Unknown"
    };

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _cpuCounter?.Dispose();
        _cpuCounter = null;

        foreach (var counter in _coreCounters)
        {
            counter.Dispose();
        }
        _coreCounters.Clear();
    }
}
