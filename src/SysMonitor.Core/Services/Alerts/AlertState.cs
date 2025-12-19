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
    /// When the alert was last triggered.
    /// </summary>
    public DateTime LastTriggered { get; set; }

    /// <summary>
    /// Whether the condition is currently active.
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
