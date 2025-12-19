using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Monitoring;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class CpuViewModel : ObservableObject, IDisposable
{
    private readonly ICpuMonitor _cpuMonitor;
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;
    private bool _isInitialized;

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
    [ObservableProperty] private string _totalUsageStatus = "Idle";
    [ObservableProperty] private double _temperature;
    [ObservableProperty] private bool _hasTemperature;
    [ObservableProperty] private string _temperatureStatus = "N/A";
    [ObservableProperty] private string _temperatureColor = "#4CAF50";

    // Per-Core Usage
    [ObservableProperty] private ObservableCollection<CoreUsageInfo> _coreUsages = new();

    // State
    [ObservableProperty] private bool _isLoading = true;

    public CpuViewModel(ICpuMonitor cpuMonitor, IPerformanceMonitor performanceMonitor)
    {
        _cpuMonitor = cpuMonitor;
        _performanceMonitor = performanceMonitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

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

        using var _ = _performanceMonitor.TrackOperation("Cpu.Refresh");

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
                TotalUsageStatus = GetUsageStatus(cpuInfo.UsagePercent);
                CurrentClockSpeedMHz = cpuInfo.CurrentClockSpeedMHz;
                CurrentClockDisplay = FormatClockSpeed(cpuInfo.CurrentClockSpeedMHz);

                // Temperature
                Temperature = temperature;
                HasTemperature = temperature > 0;
                (TemperatureStatus, TemperatureColor) = GetTemperatureStatus(temperature);

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

    private static string GetUsageStatus(double usage)
    {
        return usage switch
        {
            < 20 => "Idle",
            < 50 => "Light",
            < 75 => "Moderate",
            < 90 => "Heavy",
            _ => "Maximum"
        };
    }

    private static (string status, string color) GetTemperatureStatus(double temp)
    {
        return temp switch
        {
            0 => ("N/A", "#808080"),
            <= 45 => ("Cool", "#4CAF50"),      // Green
            <= 65 => ("Normal", "#8BC34A"),    // Light green
            <= 80 => ("Warm", "#FF9800"),      // Orange
            <= 90 => ("Hot", "#FF5722"),       // Red-orange
            _ => ("Critical", "#F44336")        // Red
        };
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
    [ObservableProperty] private string _status = "Idle";
    [ObservableProperty] private string _statusColor = "#4CAF50";

    public string CoreName => $"Core {CoreIndex}";

    partial void OnUsageChanged(double value)
    {
        // Update status based on usage level
        (Status, StatusColor) = value switch
        {
            < 20 => ("Idle", "#4CAF50"),       // Green
            < 50 => ("Light", "#8BC34A"),      // Light green
            < 75 => ("Moderate", "#FF9800"),   // Orange
            < 90 => ("Heavy", "#FF5722"),      // Red-orange
            _ => ("Max", "#F44336")            // Red
        };
    }
}
