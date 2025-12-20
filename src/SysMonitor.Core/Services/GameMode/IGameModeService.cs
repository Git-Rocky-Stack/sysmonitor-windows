namespace SysMonitor.Core.Services.GameMode;

/// <summary>
/// Result of enabling Game Mode.
/// </summary>
public class GameModeResult
{
    public bool Success { get; set; }
    public int ProcessesKilled { get; set; }
    public long MemoryFreedBytes { get; set; }
    public string? PreviousPowerPlanGuid { get; set; }
    public List<string> KilledProcessNames { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Service for managing Game Mode - optimizes system for gaming by killing
/// background apps, setting High Performance power plan, and freeing RAM.
/// </summary>
public interface IGameModeService
{
    /// <summary>
    /// Whether Game Mode is currently enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Event raised when Game Mode state changes.
    /// </summary>
    event EventHandler<bool>? GameModeChanged;

    /// <summary>
    /// Enables Game Mode: kills background apps, sets High Performance power plan, frees RAM.
    /// </summary>
    Task<GameModeResult> EnableAsync();

    /// <summary>
    /// Disables Game Mode: restores previous power plan.
    /// </summary>
    Task DisableAsync();

    /// <summary>
    /// Gets the list of process names that will be targeted for termination.
    /// </summary>
    IReadOnlyList<string> GetTargetProcesses();
}
