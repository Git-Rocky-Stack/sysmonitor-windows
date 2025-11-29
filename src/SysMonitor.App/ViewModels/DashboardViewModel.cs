using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Core.Services.Optimizers;

namespace SysMonitor.App.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly ISystemInfoService _systemInfoService;
    private readonly ITempFileCleaner _tempFileCleaner;
    private readonly IMemoryOptimizer _memoryOptimizer;
    private System.Timers.Timer? _refreshTimer;

    [ObservableProperty] private int _healthScore = 0;
    [ObservableProperty] private string _healthStatus = "Checking...";
    [ObservableProperty] private double _cpuUsage = 0;
    [ObservableProperty] private double _memoryUsage = 0;
    [ObservableProperty] private double _memoryUsedGB = 0;
    [ObservableProperty] private double _memoryTotalGB = 0;
    [ObservableProperty] private double _diskUsage = 0;
    [ObservableProperty] private double _diskUsedGB = 0;
    [ObservableProperty] private double _diskTotalGB = 0;
    [ObservableProperty] private int _batteryLevel = 0;
    [ObservableProperty] private bool _hasBattery = false;
    [ObservableProperty] private bool _isCharging = false;
    [ObservableProperty] private string _osName = "";
    [ObservableProperty] private string _uptime = "";
    [ObservableProperty] private double _cleanableSpaceMB = 0;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _isOptimizing = false;

    public DashboardViewModel(
        ISystemInfoService systemInfoService,
        ITempFileCleaner tempFileCleaner,
        IMemoryOptimizer memoryOptimizer)
    {
        _systemInfoService = systemInfoService;
        _tempFileCleaner = tempFileCleaner;
        _memoryOptimizer = memoryOptimizer;
    }

    public async Task InitializeAsync()
    {
        await RefreshDataAsync();
        StartAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _refreshTimer = new System.Timers.Timer(2000);
        _refreshTimer.Elapsed += async (s, e) => await RefreshDataAsync();
        _refreshTimer.Start();
    }

    private async Task RefreshDataAsync()
    {
        try
        {
            var info = await _systemInfoService.GetSystemInfoAsync();

            HealthScore = info.HealthScore;
            HealthStatus = info.HealthScore >= 80 ? "Excellent" :
                          info.HealthScore >= 60 ? "Good" :
                          info.HealthScore >= 40 ? "Fair" : "Poor";

            CpuUsage = info.Cpu.UsagePercent;
            MemoryUsage = info.Memory.UsagePercent;
            MemoryUsedGB = info.Memory.UsedGB;
            MemoryTotalGB = info.Memory.TotalGB;

            if (info.Disks.Count > 0)
            {
                var mainDisk = info.Disks[0];
                DiskUsage = mainDisk.UsagePercent;
                DiskUsedGB = mainDisk.UsedGB;
                DiskTotalGB = mainDisk.TotalGB;
            }

            if (info.Battery != null)
            {
                HasBattery = true;
                BatteryLevel = info.Battery.ChargePercent;
                IsCharging = info.Battery.IsCharging;
            }

            OsName = info.OperatingSystem.Name;
            Uptime = FormatUptime(info.OperatingSystem.Uptime);

            var cleanable = await _tempFileCleaner.GetTotalCleanableBytesAsync();
            CleanableSpaceMB = cleanable / (1024.0 * 1024);

            IsLoading = false;
        }
        catch { }
    }

    [RelayCommand]
    private async Task QuickCleanAsync()
    {
        IsOptimizing = true;
        try
        {
            var items = await _tempFileCleaner.ScanAsync();
            await _tempFileCleaner.CleanAsync(items);
            await RefreshDataAsync();
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    [RelayCommand]
    private async Task OptimizeMemoryAsync()
    {
        IsOptimizing = true;
        try
        {
            await _memoryOptimizer.OptimizeMemoryAsync();
            await RefreshDataAsync();
        }
        finally
        {
            IsOptimizing = false;
        }
    }

    private static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m";
        return $"{uptime.Minutes}m";
    }

    public void Dispose()
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Dispose();
    }
}
