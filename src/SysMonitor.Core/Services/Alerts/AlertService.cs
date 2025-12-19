using SysMonitor.Core.Services.Monitors;
using System.Collections.Concurrent;
using System.Text.Json;

namespace SysMonitor.Core.Services.Alerts;

/// <summary>
/// Service for monitoring system metrics against thresholds and triggering alerts.
/// Implements a 5-minute cooldown between repeat alerts of the same type.
/// </summary>
public class AlertService : IAlertService
{
    private readonly ICpuMonitor _cpuMonitor;
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly ITemperatureMonitor _temperatureMonitor;
    private readonly IBatteryMonitor _batteryMonitor;

    private readonly ConcurrentDictionary<AlertType, AlertState> _alertStates = new();
    private readonly string _settingsPath;

    public event EventHandler<AlertNotification>? AlertTriggered;

    public TimeSpan CooldownPeriod { get; set; } = TimeSpan.FromMinutes(5);

    public bool AreAlertsEnabled => GetSetting("ShowNotifications", true);

    public AlertService(
        ICpuMonitor cpuMonitor,
        IMemoryMonitor memoryMonitor,
        ITemperatureMonitor temperatureMonitor,
        IBatteryMonitor batteryMonitor)
    {
        _cpuMonitor = cpuMonitor;
        _memoryMonitor = memoryMonitor;
        _temperatureMonitor = temperatureMonitor;
        _batteryMonitor = batteryMonitor;

        _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "settings.json");
    }

    public async Task CheckThresholdsAsync()
    {
        if (!AreAlertsEnabled) return;

        var tasks = new List<Task>
        {
            CheckTemperatureAlertsAsync(),
            CheckMemoryAlertsAsync(),
            CheckBatteryAlertsAsync()
        };

        await Task.WhenAll(tasks);
    }

    private async Task CheckTemperatureAlertsAsync()
    {
        if (!GetSetting("EnableTempAlerts", true)) return;

        // CPU Temperature
        try
        {
            var cpuTemp = await _temperatureMonitor.GetCpuTemperatureAsync();
            if (cpuTemp > 0)
            {
                var cpuCritical = GetSetting("CpuTempCritical", 90);
                var cpuWarning = GetSetting("CpuTempWarning", 75);

                if (cpuTemp >= cpuCritical)
                {
                    TriggerAlert(AlertType.CpuTempCritical, AlertSeverity.Critical,
                        "CPU Temperature Critical!",
                        $"CPU temperature is {cpuTemp:F0}°C (threshold: {cpuCritical}°C)",
                        cpuTemp, cpuCritical);
                }
                else if (cpuTemp >= cpuWarning)
                {
                    TriggerAlert(AlertType.CpuTempWarning, AlertSeverity.Warning,
                        "CPU Temperature Warning",
                        $"CPU temperature is {cpuTemp:F0}°C (threshold: {cpuWarning}°C)",
                        cpuTemp, cpuWarning);
                }
                else
                {
                    ClearAlertCondition(AlertType.CpuTempWarning);
                    ClearAlertCondition(AlertType.CpuTempCritical);
                }
            }
        }
        catch { }

        // GPU Temperature
        try
        {
            var gpuTemp = await _temperatureMonitor.GetGpuTemperatureAsync();
            if (gpuTemp > 0)
            {
                var gpuCritical = GetSetting("GpuTempCritical", 95);
                var gpuWarning = GetSetting("GpuTempWarning", 80);

                if (gpuTemp >= gpuCritical)
                {
                    TriggerAlert(AlertType.GpuTempCritical, AlertSeverity.Critical,
                        "GPU Temperature Critical!",
                        $"GPU temperature is {gpuTemp:F0}°C (threshold: {gpuCritical}°C)",
                        gpuTemp, gpuCritical);
                }
                else if (gpuTemp >= gpuWarning)
                {
                    TriggerAlert(AlertType.GpuTempWarning, AlertSeverity.Warning,
                        "GPU Temperature Warning",
                        $"GPU temperature is {gpuTemp:F0}°C (threshold: {gpuWarning}°C)",
                        gpuTemp, gpuWarning);
                }
                else
                {
                    ClearAlertCondition(AlertType.GpuTempWarning);
                    ClearAlertCondition(AlertType.GpuTempCritical);
                }
            }
        }
        catch { }
    }

    private async Task CheckMemoryAlertsAsync()
    {
        try
        {
            var memInfo = await _memoryMonitor.GetMemoryInfoAsync();
            var threshold = GetSetting("MemoryThreshold", 80);

            if (memInfo.UsagePercent >= threshold)
            {
                TriggerAlert(AlertType.MemoryHigh, AlertSeverity.Warning,
                    "High Memory Usage",
                    $"Memory usage is {memInfo.UsagePercent:F0}% (threshold: {threshold}%)",
                    memInfo.UsagePercent, threshold);
            }
            else
            {
                ClearAlertCondition(AlertType.MemoryHigh);
            }
        }
        catch { }
    }

    private async Task CheckBatteryAlertsAsync()
    {
        if (!GetSetting("EnableBatteryAlerts", true)) return;

        try
        {
            var batteryInfo = await _batteryMonitor.GetBatteryInfoAsync();
            if (!batteryInfo.IsPresent || batteryInfo.IsCharging) return;

            var criticalThreshold = GetSetting("BatteryCriticalWarning", 10);
            var lowThreshold = GetSetting("BatteryLowWarning", 20);

            if (batteryInfo.ChargePercent <= criticalThreshold)
            {
                TriggerAlert(AlertType.BatteryCritical, AlertSeverity.Critical,
                    "Battery Critical!",
                    $"Battery is at {batteryInfo.ChargePercent}% - plug in immediately!",
                    batteryInfo.ChargePercent, criticalThreshold);
            }
            else if (batteryInfo.ChargePercent <= lowThreshold)
            {
                TriggerAlert(AlertType.BatteryLow, AlertSeverity.Warning,
                    "Battery Low",
                    $"Battery is at {batteryInfo.ChargePercent}% - consider plugging in.",
                    batteryInfo.ChargePercent, lowThreshold);
            }
            else
            {
                ClearAlertCondition(AlertType.BatteryLow);
                ClearAlertCondition(AlertType.BatteryCritical);
            }
        }
        catch { }
    }

    private void TriggerAlert(AlertType type, AlertSeverity severity, string title, string message, double value, double threshold)
    {
        var now = DateTime.Now;

        var state = _alertStates.GetOrAdd(type, _ => new AlertState { Type = type });

        // Check cooldown
        if (state.IsActive && (now - state.LastTriggered) < CooldownPeriod)
        {
            return; // Still in cooldown
        }

        // Update state
        state.LastTriggered = now;
        state.IsActive = true;
        state.TriggerValue = value;
        state.Threshold = threshold;

        // Raise event
        AlertTriggered?.Invoke(this, new AlertNotification
        {
            Type = type,
            Severity = severity,
            Title = title,
            Message = message,
            CurrentValue = value,
            Threshold = threshold,
            Timestamp = now
        });
    }

    private void ClearAlertCondition(AlertType type)
    {
        if (_alertStates.TryGetValue(type, out var state))
        {
            state.IsActive = false;
        }
    }

    public IReadOnlyDictionary<AlertType, AlertState> GetAlertStates()
    {
        return _alertStates.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    public void ClearAlert(AlertType type)
    {
        _alertStates.TryRemove(type, out _);
    }

    public void ClearAllAlerts()
    {
        _alertStates.Clear();
    }

    private T GetSetting<T>(string key, T defaultValue)
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                if (settings != null && settings.TryGetValue(key, out var element))
                {
                    if (typeof(T) == typeof(int) && element.TryGetInt32(out var intVal))
                        return (T)(object)intVal;
                    if (typeof(T) == typeof(bool))
                        return (T)(object)element.GetBoolean();
                }
            }
        }
        catch { }
        return defaultValue;
    }
}
