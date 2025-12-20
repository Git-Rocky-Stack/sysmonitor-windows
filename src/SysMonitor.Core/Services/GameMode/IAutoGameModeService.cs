namespace SysMonitor.Core.Services.GameMode;

/// <summary>
/// Represents a game that can be detected for auto Game Mode activation.
/// </summary>
public class GameDefinition
{
    public string ProcessName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsCustom { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Event args for game detection events.
/// </summary>
public class GameDetectedEventArgs : EventArgs
{
    public GameDefinition Game { get; set; } = null!;
    public bool WasAutoEnabled { get; set; }
}

/// <summary>
/// Service for automatically detecting running games and enabling Game Mode.
/// </summary>
public interface IAutoGameModeService
{
    /// <summary>
    /// Whether the service is actively monitoring for games.
    /// </summary>
    bool IsMonitoring { get; }

    /// <summary>
    /// Whether auto Game Mode is enabled.
    /// </summary>
    bool AutoModeEnabled { get; set; }

    /// <summary>
    /// List of predefined known games.
    /// </summary>
    IReadOnlyList<GameDefinition> KnownGames { get; }

    /// <summary>
    /// List of user-added custom games.
    /// </summary>
    IReadOnlyList<GameDefinition> CustomGames { get; }

    /// <summary>
    /// Currently detected running games.
    /// </summary>
    IReadOnlyList<GameDefinition> RunningGames { get; }

    /// <summary>
    /// Event raised when a game is detected.
    /// </summary>
    event EventHandler<GameDetectedEventArgs>? GameDetected;

    /// <summary>
    /// Event raised when a game is closed.
    /// </summary>
    event EventHandler<GameDetectedEventArgs>? GameClosed;

    /// <summary>
    /// Start monitoring for games.
    /// </summary>
    Task StartMonitoringAsync();

    /// <summary>
    /// Stop monitoring for games.
    /// </summary>
    Task StopMonitoringAsync();

    /// <summary>
    /// Add a custom game to monitor.
    /// </summary>
    Task AddCustomGameAsync(string processName, string displayName);

    /// <summary>
    /// Remove a custom game.
    /// </summary>
    Task RemoveCustomGameAsync(string processName);
}
