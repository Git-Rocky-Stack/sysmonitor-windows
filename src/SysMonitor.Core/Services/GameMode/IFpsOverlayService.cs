namespace SysMonitor.Core.Services.GameMode;

/// <summary>
/// Position for the FPS overlay on screen.
/// </summary>
public enum OverlayPosition
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

/// <summary>
/// Current stats displayed in the overlay.
/// </summary>
public class OverlayStats
{
    public int Fps { get; set; }
    public double CpuTemperature { get; set; }
    public double GpuTemperature { get; set; }
    public double CpuUsage { get; set; }
    public double GpuUsage { get; set; }
    public double RamUsageGb { get; set; }
    public double RamTotalGb { get; set; }
    public double SystemPowerWatts { get; set; }
    public double CpuPowerWatts { get; set; }
    public double GpuPowerWatts { get; set; }
    public Dictionary<string, double> FanSpeeds { get; set; } = new();
}

/// <summary>
/// Service for controlling the FPS overlay window.
/// </summary>
public interface IFpsOverlayService
{
    /// <summary>
    /// Whether the overlay is currently visible.
    /// </summary>
    bool IsVisible { get; }

    /// <summary>
    /// Current overlay position.
    /// </summary>
    OverlayPosition Position { get; set; }

    /// <summary>
    /// Update interval in milliseconds.
    /// </summary>
    int UpdateIntervalMs { get; set; }

    /// <summary>
    /// Event raised when stats are updated.
    /// </summary>
    event EventHandler<OverlayStats>? StatsUpdated;

    /// <summary>
    /// Show the overlay.
    /// </summary>
    Task ShowAsync();

    /// <summary>
    /// Hide the overlay.
    /// </summary>
    Task HideAsync();

    /// <summary>
    /// Toggle overlay visibility.
    /// </summary>
    Task ToggleAsync();
}
