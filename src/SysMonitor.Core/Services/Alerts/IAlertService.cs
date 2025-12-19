namespace SysMonitor.Core.Services.Alerts;

/// <summary>
/// Service interface for monitoring thresholds and triggering alerts.
/// </summary>
public interface IAlertService
{
    /// <summary>
    /// Event raised when an alert is triggered.
    /// </summary>
    event EventHandler<AlertNotification>? AlertTriggered;

    /// <summary>
    /// Checks current system metrics against configured thresholds and triggers alerts if needed.
    /// Should be called periodically (e.g., every 5 seconds from the dashboard refresh loop).
    /// </summary>
    Task CheckThresholdsAsync();

    /// <summary>
    /// Gets the current state of all alerts.
    /// </summary>
    IReadOnlyDictionary<AlertType, AlertState> GetAlertStates();

    /// <summary>
    /// Clears the alert state for a specific type (resets cooldown).
    /// </summary>
    void ClearAlert(AlertType type);

    /// <summary>
    /// Clears all alert states.
    /// </summary>
    void ClearAllAlerts();

    /// <summary>
    /// Gets or sets the cooldown period between repeat alerts of the same type.
    /// </summary>
    TimeSpan CooldownPeriod { get; set; }

    /// <summary>
    /// Gets whether alerts are enabled globally.
    /// </summary>
    bool AreAlertsEnabled { get; }
}
