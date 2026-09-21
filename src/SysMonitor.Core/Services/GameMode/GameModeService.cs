using System.Diagnostics;
using System.Text.RegularExpressions;
using SysMonitor.Core.Services.Optimizers;

namespace SysMonitor.Core.Services.GameMode;

/// <summary>
/// Game Mode: switches the power plan, gets background apps out of the game's way, and frees memory.
/// </summary>
/// <remarks>
/// It used to close the twenty-one apps below - browsers, Teams, Discord, OneDrive - by asking them to close
/// and killing whatever had not gone one second later, with no warning and no way to say no. A second of
/// grace is not enough to save anything, so unsaved work went with them. Now the apps are lowered out of the
/// way by default and are only ever asked to close when the user asks for that; nothing is killed.
/// </remarks>
public sealed class GameModeService : IGameModeService, IDisposable
{
    /// <summary>High Performance power plan GUID (built into Windows).</summary>
    public const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    /// <summary>The background apps Game Mode acts on when a caller names none.</summary>
    public static readonly IReadOnlyList<string> DefaultBackgroundApps =
    [
        // Browsers
        "chrome", "firefox", "msedge", "opera", "brave", "vivaldi",

        // Communication apps
        "discord", "slack", "teams", "skype", "zoom", "telegram", "whatsapp",

        // Media players
        "spotify", "itunes",

        // Cloud sync
        "onedrive", "dropbox", "googledrivesync",

        // Other background apps
        "steamwebhelper",
    ];

    private readonly IMemoryOptimizer _memoryOptimizer;
    private readonly IPowerPlanController _powerPlans;
    private readonly string _stateFilePath;
    private readonly object _gate = new();
    private readonly Dictionary<int, ProcessPriorityClass> _loweredPriorities = new();

    /// <summary>
    /// Serialises turning Game Mode on and off. Three callers can do it - the page's toggle, the
    /// auto-detect timer and a performance profile - from three different threads.
    /// </summary>
    private readonly SemaphoreSlim _transition = new(1, 1);

    // volatile: read by IsEnabled from whichever thread asks, written under _transition.
    private volatile bool _isEnabled;
    private string? _previousPowerPlanGuid;

    public GameModeService(IMemoryOptimizer memoryOptimizer)
        : this(memoryOptimizer, new PowerCfgController(), DefaultStateFilePath())
    {
    }

    internal GameModeService(IMemoryOptimizer memoryOptimizer, IPowerPlanController powerPlans, string stateFilePath)
    {
        _memoryOptimizer = memoryOptimizer;
        _powerPlans = powerPlans;
        _stateFilePath = stateFilePath;
    }

    public bool IsEnabled => _isEnabled;

    public event EventHandler<bool>? GameModeChanged;

    public IReadOnlyList<string> GetTargetProcesses() => DefaultBackgroundApps;

    public async Task<GameModeResult> EnableAsync() => await EnableAsync(new GameModeOptions());

    public async Task<GameModeResult> EnableAsync(GameModeOptions options)
    {
        var result = new GameModeResult { BackgroundAppAction = options.BackgroundApps };

        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isEnabled)
            {
                // Already on, and the plan the machine started from is already recorded. Reading the active
                // plan again here would record High Performance as the one to go back to - both in memory
                // and in the note on disk, which would then re-apply it on every launch from now on.
                result.Success = true;
                result.PreviousPowerPlanGuid = _previousPowerPlanGuid;
                return result;
            }

            _previousPowerPlanGuid = await _powerPlans.GetActiveAsync();
            result.PreviousPowerPlanGuid = _previousPowerPlanGuid;

            // Written down before the plan changes: if this process never gets to put it back, the next
            // start will.
            RememberPowerPlan(_previousPowerPlanGuid);

            if (!string.IsNullOrWhiteSpace(options.PowerPlanGuid))
            {
                await _powerPlans.SetActiveAsync(options.PowerPlanGuid);
            }

            var (affected, stillRunning) = await Task.Run(() => ApplyToBackgroundApps(options));
            result.BackgroundAppsAffected = affected;
            result.BackgroundAppsStillRunning = stillRunning;

            if (options.OptimizeMemory)
            {
                result.MemoryFreedBytes = await _memoryOptimizer.OptimizeMemoryAsync();
            }

            _isEnabled = true;
            result.Success = true;

            GameModeChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }
        finally
        {
            _transition.Release();
        }

        return result;
    }

    public async Task DisableAsync()
    {
        await _transition.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisableWhileHoldingTheGateAsync();
        }
        finally
        {
            _transition.Release();
        }
    }

    private async Task DisableWhileHoldingTheGateAsync()
    {
        RestoreLoweredPriorities();

        try
        {
            if (!string.IsNullOrEmpty(_previousPowerPlanGuid))
            {
                await _powerPlans.SetActiveAsync(_previousPowerPlanGuid);
            }
        }
        catch
        {
            // Best effort: the plan this is putting back is the one the machine already had.
        }

        ForgetPowerPlan();
        _isEnabled = false;
        _previousPowerPlanGuid = null;

        GameModeChanged?.Invoke(this, false);
    }

    public async Task<string?> RestorePowerPlanAfterCrashAsync()
    {
        var remembered = ReadRememberedPowerPlan();
        if (remembered == null || _isEnabled)
        {
            return null;
        }

        var restored = await _powerPlans.SetActiveAsync(remembered);
        ForgetPowerPlan();
        return restored ? remembered : null;
    }

    /// <summary>Puts everything back when the app closes with Game Mode still on.</summary>
    public void Dispose()
    {
        RestoreLoweredPriorities();

        if (_isEnabled && !string.IsNullOrEmpty(_previousPowerPlanGuid))
        {
            try
            {
                _powerPlans.SetActiveAsync(_previousPowerPlanGuid).GetAwaiter().GetResult();
                ForgetPowerPlan();
            }
            catch
            {
                // Best effort: leave the note on disk; the next start will put the plan back.
            }
        }

        _isEnabled = false;
    }

    private (List<string> Affected, List<string> StillRunning) ApplyToBackgroundApps(GameModeOptions options)
    {
        var affected = new List<string>();
        var stillRunning = new List<string>();

        if (options.BackgroundApps == BackgroundAppAction.LeaveAlone)
        {
            return (affected, stillRunning);
        }

        foreach (var name in options.BackgroundAppNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            foreach (var process in processes)
            {
                try
                {
                    var done = options.BackgroundApps == BackgroundAppAction.LowerPriority
                        ? LowerPriority(process)
                        : AskToClose(process, options.CloseTimeout);

                    (done ? affected : stillRunning).Add(name);
                }
                catch
                {
                    // Best effort: a process this app may not touch is left alone.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        return (
            affected.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            stillRunning.Distinct(StringComparer.OrdinalIgnoreCase).Except(affected, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>Moves an app below the game in the queue for the processor, remembering where it was.</summary>
    private bool LowerPriority(Process process)
    {
        var current = process.PriorityClass;
        if (current is ProcessPriorityClass.Idle or ProcessPriorityClass.BelowNormal)
        {
            return false;
        }

        process.PriorityClass = ProcessPriorityClass.BelowNormal;
        lock (_gate)
        {
            _loweredPriorities[process.Id] = current;
        }

        return true;
    }

    /// <summary>
    /// Asks an app to close, as clicking its X does, and waits. One with no window to ask, or one that stays
    /// open, is left running: what it holds belongs to the user, not to Game Mode.
    /// </summary>
    private static bool AskToClose(Process process, TimeSpan timeout)
    {
        if (!process.CloseMainWindow())
        {
            return false;
        }

        return process.WaitForExit((int)Math.Max(0, timeout.TotalMilliseconds));
    }

    private void RestoreLoweredPriorities()
    {
        KeyValuePair<int, ProcessPriorityClass>[] lowered;
        lock (_gate)
        {
            lowered = _loweredPriorities.ToArray();
            _loweredPriorities.Clear();
        }

        foreach (var (processId, priority) in lowered)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    process.PriorityClass = priority;
                }
            }
            catch
            {
                // Best effort: it has gone, or it is not ours to change any more.
            }
        }
    }

    private static string DefaultStateFilePath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SysMonitor",
        "game-mode-power-plan.txt");

    private void RememberPowerPlan(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid)) return;

        try
        {
            var folder = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(folder))
            {
                Directory.CreateDirectory(folder);
            }

            File.WriteAllText(_stateFilePath, guid);
        }
        catch
        {
            // Best effort: without the note, only this process can put the plan back - which it
            // still does, from memory, for as long as it is running.
        }
    }

    private string? ReadRememberedPowerPlan()
    {
        try
        {
            if (!File.Exists(_stateFilePath)) return null;

            var text = File.ReadAllText(_stateFilePath).Trim();
            return Guid.TryParse(text, out _) ? text : null;
        }
        catch
        {
            return null;
        }
    }

    private void ForgetPowerPlan()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                File.Delete(_stateFilePath);
            }
        }
        catch
        {
            // Best effort: a note left behind only costs one extra restore next time.
        }
    }
}

/// <summary>Reads and sets the Windows power plan with powercfg.</summary>
internal sealed class PowerCfgController : IPowerPlanController
{
    public async Task<string?> GetActiveAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = "/getactivescheme",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return null;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)"
                var match = Regex.Match(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
                return match.Success ? match.Value : null;
            }
            catch
            {
                return null;
            }
        });
    }

    public async Task<bool> SetActiveAsync(string guid)
    {
        return await Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powercfg",
                    Arguments = $"/setactive {guid}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return false;

                process.WaitForExit();
                return process.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        });
    }
}
