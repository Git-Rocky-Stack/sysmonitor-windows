namespace SysMonitor.Core.Services.GameMode;

/// <summary>What Game Mode does with the background apps it is given.</summary>
public enum BackgroundAppAction
{
    /// <summary>Leave them running, untouched.</summary>
    LeaveAlone,

    /// <summary>
    /// Drop them below the game in the queue for the processor. Nothing closes, nothing is lost, and their
    /// priorities go back where they were when Game Mode ends.
    /// </summary>
    LowerPriority,

    /// <summary>
    /// Ask them to close, as clicking the X does. One that will not close - because it has unsaved work, or
    /// no window to ask - is left running and reported. Nothing is ever killed.
    /// </summary>
    AskToClose,
}

/// <summary>What a Game Mode session should do.</summary>
public sealed record GameModeOptions
{
    /// <summary>What to do with the background apps. Lowering priority is the default because it loses nothing.</summary>
    public BackgroundAppAction BackgroundApps { get; init; } = BackgroundAppAction.LowerPriority;

    /// <summary>The apps to act on; the built-in list when a caller names none.</summary>
    public IReadOnlyList<string> BackgroundAppNames { get; init; } = GameModeService.DefaultBackgroundApps;

    public bool OptimizeMemory { get; init; } = true;

    /// <summary>The power plan to switch to, or empty to leave the plan alone.</summary>
    public string PowerPlanGuid { get; init; } = GameModeService.HighPerformanceGuid;

    /// <summary>How long an app gets to close before it is left running.</summary>
    public TimeSpan CloseTimeout { get; init; } = TimeSpan.FromSeconds(10);
}

/// <summary>What enabling Game Mode did.</summary>
public class GameModeResult
{
    public bool Success { get; set; }

    /// <summary>What was done with the background apps.</summary>
    public BackgroundAppAction BackgroundAppAction { get; set; }

    /// <summary>Apps whose priority was lowered, or that closed when asked.</summary>
    public List<string> BackgroundAppsAffected { get; set; } = new();

    /// <summary>Apps that were asked to close and kept running. They are left alone.</summary>
    public List<string> BackgroundAppsStillRunning { get; set; } = new();

    public int ProcessesAffected => BackgroundAppsAffected.Count;

    /// <summary>
    /// Bytes trimmed out of background app working sets, not bytes of RAM made available. Windows
    /// moves those pages to the standby list and can page them back as soon as the app touches them.
    /// </summary>
    public long MemoryTrimmedBytes { get; set; }
    public string? PreviousPowerPlanGuid { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>Reads and sets the Windows power plan.</summary>
public interface IPowerPlanController
{
    /// <summary>The GUID of the active plan, or null when it cannot be read.</summary>
    Task<string?> GetActiveAsync();

    /// <summary>Switches to a plan. False when it did not happen.</summary>
    Task<bool> SetActiveAsync(string guid);
}

/// <summary>
/// Game Mode: gives the game the machine's attention by lowering background apps out of the way, switching
/// the power plan, and freeing memory. It never closes anything the user did not ask it to close.
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

    /// <summary>Enables Game Mode with the default settings: background apps lowered, nothing closed.</summary>
    Task<GameModeResult> EnableAsync();

    /// <summary>Enables Game Mode as asked - which apps, what to do with them, which power plan.</summary>
    Task<GameModeResult> EnableAsync(GameModeOptions options);

    /// <summary>Disables Game Mode: puts the priorities and the power plan back.</summary>
    Task DisableAsync();

    /// <summary>
    /// Puts the power plan back after a session that never ended properly - a crash, or the machine going
    /// down with Game Mode on. Returns the plan restored, or null when there was nothing to put back.
    /// </summary>
    Task<string?> RestorePowerPlanAfterCrashAsync();

    /// <summary>
    /// Gets the list of background apps Game Mode acts on when a caller names none.
    /// </summary>
    IReadOnlyList<string> GetTargetProcesses();
}
