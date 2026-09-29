using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SysMonitor.Core.Helpers;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Settings;
using System.Collections.Concurrent;

namespace SysMonitor.Core.Services.Alerts;

/// <summary>
/// Service for monitoring system metrics against thresholds and triggering alerts.
/// Implements a 5-minute cooldown between repeat alerts of the same type.
/// </summary>
public class AlertService : IAlertService
{
    private readonly ILogger _logger;

    private readonly ICpuMonitor _cpuMonitor;
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly ITemperatureMonitor _temperatureMonitor;
    private readonly IBatteryMonitor _batteryMonitor;

    private readonly ConcurrentDictionary<AlertType, AlertState> _alertStates = new();
    private readonly ISettingsStore _settings;
    private readonly TimeProvider _timeProvider;

    public event EventHandler<AlertNotification>? AlertTriggered;

    public TimeSpan CooldownPeriod { get; set; } = TimeSpan.FromMinutes(5);

    public bool AreAlertsEnabled => GetSetting("ShowNotifications", true);

    /// <param name="settingsPath">
    /// Where the thresholds live. Defaults to the per-user file the app writes; a test passes its own so it
    /// never reads the developer's real settings.
    /// </param>
    /// <param name="timeProvider">
    /// The clock the cooldown is measured on. A test can move it; nothing else needs to.
    /// </param>
    /// <param name="settings">
    /// The store the Settings page writes to. The app passes the one every other part of it reads, which is
    /// what makes a toggle on that page reach this service in the packaged build as well as the unpackaged one.
    /// </param>
    public AlertService(
        ICpuMonitor cpuMonitor,
        IMemoryMonitor memoryMonitor,
        ITemperatureMonitor temperatureMonitor,
        IBatteryMonitor batteryMonitor,
        ILogger<AlertService>? logger = null,
        string? settingsPath = null,
        TimeProvider? timeProvider = null,
        ISettingsStore? settings = null)
    {
        _logger = logger ?? NullLogger<AlertService>.Instance;
        _cpuMonitor = cpuMonitor;
        _memoryMonitor = memoryMonitor;
        _temperatureMonitor = temperatureMonitor;
        _batteryMonitor = batteryMonitor;
        _timeProvider = timeProvider ?? TimeProvider.System;

        _settings = settingsPath is not null
            ? new SettingsStore(settingsPath, _logger)
            : settings ?? new SettingsStore();
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
                        $"CPU temperature is {FormatHelper.CelsiusToFahrenheit(cpuTemp):F0}°F (threshold: {FormatHelper.CelsiusToFahrenheit(cpuCritical):F0}°F)",
                        cpuTemp, cpuCritical);
                }
                else if (cpuTemp >= cpuWarning)
                {
                    TriggerAlert(AlertType.CpuTempWarning, AlertSeverity.Warning,
                        "CPU Temperature Warning",
                        $"CPU temperature is {FormatHelper.CelsiusToFahrenheit(cpuTemp):F0}°F (threshold: {FormatHelper.CelsiusToFahrenheit(cpuWarning):F0}°F)",
                        cpuTemp, cpuWarning);
                }
                else
                {
                    ClearAlertCondition(AlertType.CpuTempWarning);
                    ClearAlertCondition(AlertType.CpuTempCritical);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CheckTemperatureAlertsAsync failed");
        }

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
                        $"GPU temperature is {FormatHelper.CelsiusToFahrenheit(gpuTemp):F0}°F (threshold: {FormatHelper.CelsiusToFahrenheit(gpuCritical):F0}°F)",
                        gpuTemp, gpuCritical);
                }
                else if (gpuTemp >= gpuWarning)
                {
                    TriggerAlert(AlertType.GpuTempWarning, AlertSeverity.Warning,
                        "GPU Temperature Warning",
                        $"GPU temperature is {FormatHelper.CelsiusToFahrenheit(gpuTemp):F0}°F (threshold: {FormatHelper.CelsiusToFahrenheit(gpuWarning):F0}°F)",
                        gpuTemp, gpuWarning);
                }
                else
                {
                    ClearAlertCondition(AlertType.GpuTempWarning);
                    ClearAlertCondition(AlertType.GpuTempCritical);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CheckTemperatureAlertsAsync failed");
        }
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CheckMemoryAlertsAsync failed");
        }
    }

    private async Task CheckBatteryAlertsAsync()
    {
        if (!GetSetting("EnableBatteryAlerts", true)) return;

        try
        {
            // A machine with no battery reports none at all, which is not the same as a battery at 0%.
            var batteryInfo = await _batteryMonitor.GetBatteryInfoAsync();
            if (batteryInfo is null || !batteryInfo.IsPresent || batteryInfo.IsCharging) return;

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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "CheckBatteryAlertsAsync failed");
        }
    }

    private void TriggerAlert(AlertType type, AlertSeverity severity, string title, string message, double value, double threshold)
    {
        // UTC, not local: the cooldown is a length of real time, and local time repeats an hour every
        // autumn. On DateTime.Now the elapsed time went negative for that hour, which reads as "inside the
        // cooldown" and silenced every alert on the machine until it was over.
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var state = _alertStates.GetOrAdd(type, _ => new AlertState { Type = type });

        var sinceLast = now - state.LastTriggeredUtc;

        // The cooldown is on the alert, not on the condition holding unbroken. It used to also require
        // state.IsActive, and the automatic clear-down turns that off without touching the timestamp - so a
        // metric flickering across its threshold re-armed the alert on every dip and produced a toast per
        // poll. ClearAlert(type) is the documented way to ask to be told again sooner.
        //
        // A negative elapsed time means the clock was moved back under us. Say it anyway: a repeat toast
        // costs less than a missed critical temperature.
        if (state.HasTriggered && sinceLast >= TimeSpan.Zero && sinceLast < CooldownPeriod)
        {
            return; // Still in cooldown
        }

        // Update state
        state.LastTriggeredUtc = now;
        state.HasTriggered = true;
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
            Timestamp = _timeProvider.GetLocalNow().DateTime
        });
    }

    /// <remarks>
    /// Records that the metric came back inside its threshold. It deliberately leaves
    /// <see cref="AlertState.LastTriggeredUtc"/> alone: the cooldown counts from the last time the user was
    /// told, and a value that dips under the line for one poll has not told them anything new.
    /// </remarks>
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

    private T GetSetting<T>(string key, T defaultValue) => _settings.Get(key, defaultValue);
}
