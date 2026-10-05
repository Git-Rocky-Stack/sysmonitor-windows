using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Monitors;
using Serilog;

namespace SysMonitor.App.ViewModels;

public partial class BatteryViewModel : ObservableObject, IDisposable
{
    private readonly IBatteryMonitor _batteryMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;
    private bool _isInitialized;

    // Battery Presence
    [ObservableProperty] private bool _hasBattery;
    [ObservableProperty] private bool _noBattery;

    // Charge Status
    [ObservableProperty] private int _chargePercent;
    [ObservableProperty] private bool _isCharging;
    [ObservableProperty] private bool _isPluggedIn;
    [ObservableProperty] private string _chargingStatus = "Checking...";
    [ObservableProperty] private LampState _chargeStatusState = LampState.Go;
    [ObservableProperty] private string _chargeLevelStatus = "Checking...";
    [ObservableProperty] private LampState _chargingStatusState = LampState.Off;

    // Runtime
    [ObservableProperty] private string _estimatedRuntime = "";
    [ObservableProperty] private bool _hasEstimatedRuntime;

    // Health
    [ObservableProperty] private string _healthStatus = "";
    [ObservableProperty] private LampState _healthState = LampState.Go;

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
            // Best effort: the page was left while this was in flight, so the work it was doing
            // no longer has anywhere to go.
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

                // Status text and state
                if (batteryInfo.IsCharging)
                {
                    ChargingStatus = "Charging";
                    ChargingStatusState = LampState.Go;
                }
                else if (batteryInfo.IsPluggedIn)
                {
                    ChargingStatus = "Plugged in, not charging";
                    ChargingStatusState = LampState.Exec;
                }
                else
                {
                    ChargingStatus = "On battery power";
                    ChargingStatusState = LampState.Hold;
                }

                // Charge level status and state
                (ChargeLevelStatus, ChargeStatusState) = GetChargeLevelStatus(batteryInfo.ChargePercent);

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
                HealthState = GetHealthState(batteryInfo.HealthStatus);

                // Battery icon
                BatteryIcon = GetBatteryIcon(batteryInfo.ChargePercent, batteryInfo.IsCharging);

                IsLoading = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Best effort: the app is closing and the refresh was cancelled on purpose.
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Refreshing the battery page failed");
        }
    }

    private static string FormatRuntime(TimeSpan runtime)
    {
        if (runtime.TotalHours >= 1)
            return $"{(int)runtime.TotalHours}h {runtime.Minutes}m";
        return $"{runtime.Minutes}m";
    }

    private static LampState GetHealthState(string health)
    {
        // These are the words BatteryMonitor uses for how worn the battery is. "Low" and "Critical" were
        // among them while this showed the charge level instead.
        return health switch
        {
            "Good" => LampState.Go,
            "Fair" => LampState.Hold,
            "Worn" => LampState.Warn,
            "Poor" => LampState.NoGo,
            _ => LampState.Off          // not reported
        };
    }

    private static (string status, LampState state) GetChargeLevelStatus(int percent)
    {
        return percent switch
        {
            >= 80 => ("Excellent", LampState.Go),
            >= 50 => ("Good", LampState.Go),
            >= 20 => ("Low", LampState.Hold),
            >= 10 => ("Very Low", LampState.Warn),
            _ => ("Critical", LampState.NoGo)
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
