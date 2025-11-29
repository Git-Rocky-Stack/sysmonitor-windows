using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Monitors;

namespace SysMonitor.App.ViewModels;

public partial class BatteryViewModel : ObservableObject, IDisposable
{
    private readonly IBatteryMonitor _batteryMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;

    // Battery Presence
    [ObservableProperty] private bool _hasBattery;
    [ObservableProperty] private bool _noBattery;

    // Charge Status
    [ObservableProperty] private int _chargePercent;
    [ObservableProperty] private bool _isCharging;
    [ObservableProperty] private bool _isPluggedIn;
    [ObservableProperty] private string _chargingStatus = "Checking...";
    [ObservableProperty] private string _chargeStatusColor = "#4CAF50";
    [ObservableProperty] private string _chargeLevelStatus = "Checking...";
    [ObservableProperty] private string _chargingStatusColor = "#808080";

    // Runtime
    [ObservableProperty] private string _estimatedRuntime = "";
    [ObservableProperty] private bool _hasEstimatedRuntime;

    // Health
    [ObservableProperty] private string _healthStatus = "";
    [ObservableProperty] private string _healthColor = "#4CAF50";

    // Status Icon
    [ObservableProperty] private string _batteryIcon = "\uE83F";

    // State
    [ObservableProperty] private bool _isLoading = true;

    public BatteryViewModel(IBatteryMonitor batteryMonitor)
    {
        _batteryMonitor = batteryMonitor;
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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5)); // Battery changes slowly
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
            var batteryInfo = await _batteryMonitor.GetBatteryInfoAsync();
            if (_isDisposed) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                if (batteryInfo == null)
                {
                    HasBattery = false;
                    NoBattery = true;
                    ChargingStatus = "No battery detected";
                    IsLoading = false;
                    return;
                }

                HasBattery = true;
                NoBattery = false;

                // Charge info
                ChargePercent = batteryInfo.ChargePercent;
                IsCharging = batteryInfo.IsCharging;
                IsPluggedIn = batteryInfo.IsPluggedIn;

                // Status text and colors
                if (batteryInfo.IsCharging)
                {
                    ChargingStatus = "Charging";
                    ChargingStatusColor = "#4CAF50"; // Green
                }
                else if (batteryInfo.IsPluggedIn)
                {
                    ChargingStatus = "Plugged in, not charging";
                    ChargingStatusColor = "#00BCD4"; // Cyan
                }
                else
                {
                    ChargingStatus = "On battery power";
                    ChargingStatusColor = "#FF9800"; // Orange
                }

                // Charge level status and color
                (ChargeLevelStatus, ChargeStatusColor) = GetChargeLevelStatus(batteryInfo.ChargePercent);

                // Estimated runtime
                if (batteryInfo.EstimatedRuntime > TimeSpan.Zero && !batteryInfo.IsPluggedIn)
                {
                    HasEstimatedRuntime = true;
                    EstimatedRuntime = FormatRuntime(batteryInfo.EstimatedRuntime);
                }
                else
                {
                    HasEstimatedRuntime = false;
                    EstimatedRuntime = batteryInfo.IsPluggedIn ? "Plugged in" : "Calculating...";
                }

                // Health status
                HealthStatus = batteryInfo.HealthStatus;
                HealthColor = GetHealthColor(batteryInfo.HealthStatus);

                // Battery icon
                BatteryIcon = GetBatteryIcon(batteryInfo.ChargePercent, batteryInfo.IsCharging);

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

    private static string FormatRuntime(TimeSpan runtime)
    {
        if (runtime.TotalHours >= 1)
            return $"{(int)runtime.TotalHours}h {runtime.Minutes}m";
        return $"{runtime.Minutes}m";
    }

    private static string GetHealthColor(string health)
    {
        return health switch
        {
            "Good" => "#4CAF50",    // Green
            "Fair" => "#FF9800",    // Orange
            "Low" => "#FF5722",     // Deep Orange
            "Critical" => "#F44336", // Red
            _ => "#808080"          // Gray
        };
    }

    private static (string status, string color) GetChargeLevelStatus(int percent)
    {
        return percent switch
        {
            >= 80 => ("Excellent", "#4CAF50"),      // Green
            >= 50 => ("Good", "#8BC34A"),           // Light green
            >= 20 => ("Low", "#FF9800"),            // Orange
            >= 10 => ("Very Low", "#FF5722"),       // Deep orange
            _ => ("Critical", "#F44336")             // Red
        };
    }

    private static string GetBatteryIcon(int percent, bool isCharging)
    {
        if (isCharging) return "\uEA93"; // Battery charging icon

        return percent switch
        {
            >= 90 => "\uE83F", // Full
            >= 70 => "\uE859", // High
            >= 40 => "\uE857", // Medium
            >= 20 => "\uE855", // Low
            _ => "\uE851"      // Critical
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
