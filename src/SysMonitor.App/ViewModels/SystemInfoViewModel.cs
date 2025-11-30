using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services;

namespace SysMonitor.App.ViewModels;

public partial class SystemInfoViewModel : ObservableObject, IDisposable
{
    private readonly ISystemInfoService _systemInfoService;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;
    private bool _isInitialized;

    // Operating System
    [ObservableProperty] private string _osName = "";
    [ObservableProperty] private string _osVersion = "";
    [ObservableProperty] private string _osBuild = "";
    [ObservableProperty] private string _osArchitecture = "";
    [ObservableProperty] private string _computerName = "";
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _installDate = "";
    [ObservableProperty] private string _uptime = "";

    // CPU
    [ObservableProperty] private string _cpuName = "";
    [ObservableProperty] private string _cpuManufacturer = "";
    [ObservableProperty] private int _cpuCores;
    [ObservableProperty] private int _cpuThreads;
    [ObservableProperty] private string _cpuSpeed = "";
    [ObservableProperty] private string _cpuArchitecture = "";
    [ObservableProperty] private string _cpuCacheL2 = "";
    [ObservableProperty] private string _cpuCacheL3 = "";

    // Memory
    [ObservableProperty] private string _totalMemory = "";
    [ObservableProperty] private string _usedMemory = "";
    [ObservableProperty] private string _freeMemory = "";
    [ObservableProperty] private double _memoryUsagePercent;

    // Storage Summary
    [ObservableProperty] private string _totalStorage = "";
    [ObservableProperty] private string _usedStorage = "";
    [ObservableProperty] private string _freeStorage = "";
    [ObservableProperty] private int _driveCount;

    // Health Score
    [ObservableProperty] private int _healthScore;
    [ObservableProperty] private string _healthStatus = "";
    [ObservableProperty] private string _healthColor = "#4CAF50";

    // State
    [ObservableProperty] private bool _isLoading = true;

    public SystemInfoViewModel(ISystemInfoService systemInfoService)
    {
        _systemInfoService = systemInfoService;
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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30)); // System info changes slowly
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
            var systemInfo = await _systemInfoService.GetSystemInfoAsync();
            if (_isDisposed) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                // Operating System
                OsName = systemInfo.OperatingSystem.Name;
                OsVersion = systemInfo.OperatingSystem.Version;
                OsBuild = systemInfo.OperatingSystem.Build;
                OsArchitecture = systemInfo.OperatingSystem.Architecture;
                ComputerName = systemInfo.OperatingSystem.ComputerName;
                UserName = systemInfo.OperatingSystem.UserName;
                InstallDate = systemInfo.OperatingSystem.InstallDate > DateTime.MinValue
                    ? systemInfo.OperatingSystem.InstallDate.ToString("MMMM d, yyyy")
                    : "Unknown";
                Uptime = FormatUptime(systemInfo.OperatingSystem.Uptime);

                // CPU
                CpuName = systemInfo.Cpu.Name;
                CpuManufacturer = systemInfo.Cpu.Manufacturer;
                CpuCores = systemInfo.Cpu.Cores;
                CpuThreads = systemInfo.Cpu.LogicalProcessors;
                CpuSpeed = $"{systemInfo.Cpu.MaxClockSpeedMHz / 1000:F2} GHz";
                CpuArchitecture = systemInfo.Cpu.Architecture;
                CpuCacheL2 = systemInfo.Cpu.CacheL2;
                CpuCacheL3 = systemInfo.Cpu.CacheL3;

                // Memory
                TotalMemory = $"{systemInfo.Memory.TotalGB:F1} GB";
                UsedMemory = $"{systemInfo.Memory.UsedGB:F1} GB";
                FreeMemory = $"{systemInfo.Memory.AvailableGB:F1} GB";
                MemoryUsagePercent = systemInfo.Memory.UsagePercent;

                // Storage Summary
                double totalStorageGB = 0;
                double usedStorageGB = 0;
                double freeStorageGB = 0;
                foreach (var disk in systemInfo.Disks)
                {
                    totalStorageGB += disk.TotalGB;
                    usedStorageGB += disk.UsedGB;
                    freeStorageGB += disk.FreeGB;
                }
                TotalStorage = FormatStorageSize(totalStorageGB);
                UsedStorage = FormatStorageSize(usedStorageGB);
                FreeStorage = FormatStorageSize(freeStorageGB);
                DriveCount = systemInfo.Disks.Count;

                // Health Score
                HealthScore = systemInfo.HealthScore;
                (HealthStatus, HealthColor) = GetHealthStatus(systemInfo.HealthScore);

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

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{(int)uptime.TotalHours}h {uptime.Minutes}m";
        return $"{uptime.Minutes}m";
    }

    private static string FormatStorageSize(double gb)
    {
        if (gb >= 1000)
            return $"{gb / 1000:F2} TB";
        return $"{gb:F1} GB";
    }

    private static (string status, string color) GetHealthStatus(int score)
    {
        return score switch
        {
            >= 90 => ("Excellent", "#4CAF50"),
            >= 75 => ("Good", "#8BC34A"),
            >= 60 => ("Fair", "#FF9800"),
            >= 40 => ("Poor", "#FF5722"),
            _ => ("Critical", "#F44336")
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
