namespace SysMonitor.Core.Services.Alerts;

/// <summary>
/// Defines the types of alerts that can be triggered.
/// </summary>
public enum AlertType
{
    CpuTempWarning,
    CpuTempCritical,
    GpuTempWarning,
    GpuTempCritical,
    MemoryHigh,
    BatteryLow,
    BatteryCritical
}

/// <summary>
/// Defines alert severity levels.
/// </summary>
public enum AlertSeverity
{
    Warning,
    Critical
}

/// <summary>
/// Represents the state of an active alert for cooldown tracking.
/// </summary>
public class AlertState
{
    /// <summary>
    /// The type of alert.
    /// </summary>
    public AlertType Type { get; set; }

    /// <summary>
    /// When the user was last told about this alert, in UTC. The cooldown is a length of real time, so it
    /// is never measured on local time — that repeats an hour every autumn.
    /// </summary>
    public DateTime LastTriggeredUtc { get; set; }

    /// <summary>
    /// Whether the user has ever been told about this alert. Distinguishes "never fired" from
    /// <see cref="LastTriggeredUtc"/> happening to hold a default value.
    /// </summary>
    public bool HasTriggered { get; set; }

    /// <summary>
    /// Whether the condition is currently active — i.e. the metric is still outside its threshold. This is
    /// for display; it is not part of the cooldown, because a metric flickering across the line would
    /// otherwise re-arm the alert on every dip.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// The value that triggered the alert.
    /// </summary>
    public double TriggerValue { get; set; }

    /// <summary>
    /// The threshold that was exceeded.
    /// </summary>
    public double Threshold { get; set; }
}

/// <summary>
/// Data for displaying an alert notification.
/// </summary>
public class AlertNotification
{
    public AlertType Type { get; set; }
    public AlertSeverity Severity { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public double CurrentValue { get; set; }
    public double Threshold { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}
