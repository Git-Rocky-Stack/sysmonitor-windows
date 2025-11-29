using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Monitors;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class CpuViewModel : ObservableObject, IDisposable
{
    private readonly ICpuMonitor _cpuMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;

    // CPU Identification
    [ObservableProperty] private string _cpuName = "Loading...";
    [ObservableProperty] private string _manufacturer = "";
    [ObservableProperty] private string _architecture = "";

    // Core/Thread Info
    [ObservableProperty] private int _physicalCores;
    [ObservableProperty] private int _logicalProcessors;

    // Clock Speeds
    [ObservableProperty] private double _maxClockSpeedMHz;
    [ObservableProperty] private double _currentClockSpeedMHz;
    [ObservableProperty] private string _maxClockDisplay = "";
    [ObservableProperty] private string _currentClockDisplay = "";

    // Cache Info
    [ObservableProperty] private string _cacheL2 = "";
    [ObservableProperty] private string _cacheL3 = "";

    // Usage
    [ObservableProperty] private double _totalUsage;
    [ObservableProperty] private double _temperature;
    [ObservableProperty] private bool _hasTemperature;

    // Per-Core Usage
    [ObservableProperty] private ObservableCollection<CoreUsageInfo> _coreUsages = new();

    // State
    [ObservableProperty] private bool _isLoading = true;

    public CpuViewModel(ICpuMonitor cpuMonitor)
    {
        _cpuMonitor = cpuMonitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        await RefreshDataAsync();
        StartAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _cts = new CancellationTokenSource();
        _ = RefreshLoopAsync(_cts.Token);
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshDataAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disposed
        }
    }

    private async Task RefreshDataAsync()
    {
        if (_isDisposed) return;

        try
        {
            var cpuInfo = await _cpuMonitor.GetCpuInfoAsync();
            var temperature = await _cpuMonitor.GetTemperatureAsync();
            if (_isDisposed) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                // Update static info (only changes once at startup)
                if (IsLoading)
                {
                    CpuName = cpuInfo.Name;
                    Manufacturer = cpuInfo.Manufacturer;
                    Architecture = cpuInfo.Architecture;
                    PhysicalCores = cpuInfo.Cores;
                    LogicalProcessors = cpuInfo.LogicalProcessors;
                    MaxClockSpeedMHz = cpuInfo.MaxClockSpeedMHz;
                    MaxClockDisplay = FormatClockSpeed(cpuInfo.MaxClockSpeedMHz);
                    CacheL2 = cpuInfo.CacheL2;
                    CacheL3 = cpuInfo.CacheL3;
                }

                // Update dynamic info
                TotalUsage = cpuInfo.UsagePercent;
                CurrentClockSpeedMHz = cpuInfo.CurrentClockSpeedMHz;
                CurrentClockDisplay = FormatClockSpeed(cpuInfo.CurrentClockSpeedMHz);

                // Temperature
                Temperature = temperature;
                HasTemperature = temperature > 0;

                // Update per-core usages
                UpdateCoreUsages(cpuInfo.CoreUsages);

                IsLoading = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception)
        {
            // Log in production
        }
    }

    private void UpdateCoreUsages(List<double> usages)
    {
        // Initialize collection if needed
        while (CoreUsages.Count < usages.Count)
        {
            CoreUsages.Add(new CoreUsageInfo { CoreIndex = CoreUsages.Count });
        }

        // Update values
        for (int i = 0; i < usages.Count; i++)
        {
            CoreUsages[i].Usage = usages[i];
        }
    }

    private static string FormatClockSpeed(double mhz)
    {
        if (mhz >= 1000)
            return $"{mhz / 1000:F2} GHz";
        return $"{mhz:F0} MHz";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

/// <summary>
/// Represents usage information for a single CPU core.
/// </summary>
public partial class CoreUsageInfo : ObservableObject
{
    [ObservableProperty] private int _coreIndex;
    [ObservableProperty] private double _usage;

    public string CoreName => $"Core {CoreIndex}";
}
