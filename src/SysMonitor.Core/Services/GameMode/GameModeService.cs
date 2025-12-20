using System.Diagnostics;
using System.Text.RegularExpressions;
using SysMonitor.Core.Services.Optimizers;

namespace SysMonitor.Core.Services.GameMode;

/// <summary>
/// Service that enables Game Mode for optimized gaming performance.
/// Kills background apps, sets High Performance power plan, and frees RAM.
/// </summary>
public class GameModeService : IGameModeService
{
    private readonly IMemoryOptimizer _memoryOptimizer;
    private bool _isEnabled;
    private string? _previousPowerPlanGuid;

    // High Performance power plan GUID (built into Windows)
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

    // Predefined list of processes to kill when Game Mode is enabled
    private static readonly string[] TargetProcessNames = new[]
    {
        // Browsers
        "chrome",
        "firefox",
        "msedge",
        "opera",
        "brave",
        "vivaldi",

        // Communication apps
        "discord",
        "slack",
        "teams",
        "skype",
        "zoom",
        "telegram",
        "whatsapp",

        // Media players
        "spotify",
        "itunes",

        // Cloud sync
        "onedrive",
        "dropbox",
        "googledrivesync",

        // Other background apps
        "steamwebhelper"  // Steam web helper (not main Steam process)
    };

    public bool IsEnabled => _isEnabled;

    public event EventHandler<bool>? GameModeChanged;

    public GameModeService(IMemoryOptimizer memoryOptimizer)
    {
        _memoryOptimizer = memoryOptimizer;
    }

    public async Task<GameModeResult> EnableAsync()
    {
        var result = new GameModeResult();

        try
        {
            // 1. Save current power plan
            _previousPowerPlanGuid = await GetCurrentPowerPlanGuidAsync();
            result.PreviousPowerPlanGuid = _previousPowerPlanGuid;

            // 2. Set High Performance power plan
            await SetPowerPlanAsync(HighPerformanceGuid);

            // 3. Kill target processes
            var killedProcesses = await KillTargetProcessesAsync();
            result.ProcessesKilled = killedProcesses.Count;
            result.KilledProcessNames = killedProcesses;

            // 4. Optimize memory (free RAM)
            result.MemoryFreedBytes = await _memoryOptimizer.OptimizeMemoryAsync();

            _isEnabled = true;
            result.Success = true;

            GameModeChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    public async Task DisableAsync()
    {
        try
        {
            // Restore previous power plan if we have it
            if (!string.IsNullOrEmpty(_previousPowerPlanGuid))
            {
                await SetPowerPlanAsync(_previousPowerPlanGuid);
            }
        }
        catch
        {
            // Ignore errors when restoring
        }

        _isEnabled = false;
        _previousPowerPlanGuid = null;

        GameModeChanged?.Invoke(this, false);
    }

    public IReadOnlyList<string> GetTargetProcesses()
    {
        return TargetProcessNames;
    }

    private async Task<string?> GetCurrentPowerPlanGuidAsync()
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

                // Parse GUID from output like: "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)"
                var match = Regex.Match(output, @"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})");
                return match.Success ? match.Value : null;
            }
            catch
            {
                return null;
            }
        });
    }

    private async Task SetPowerPlanAsync(string guid)
    {
        await Task.Run(() =>
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
                process?.WaitForExit();
            }
            catch
            {
                // Ignore errors
            }
        });
    }

    private async Task<List<string>> KillTargetProcessesAsync()
    {
        return await Task.Run(() =>
        {
            var killedProcesses = new List<string>();

            foreach (var processName in TargetProcessNames)
            {
                try
                {
                    var processes = Process.GetProcessesByName(processName);
                    foreach (var process in processes)
                    {
                        try
                        {
                            // Try graceful close first
                            if (process.CloseMainWindow())
                            {
                                // Wait a short time for graceful close
                                if (!process.WaitForExit(1000))
                                {
                                    // Force kill if didn't close gracefully
                                    process.Kill();
                                }
                            }
                            else
                            {
                                // No main window, force kill
                                process.Kill();
                            }

                            killedProcesses.Add(processName);
                        }
                        catch
                        {
                            // Ignore individual process kill failures
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch
                {
                    // Ignore errors getting process list
                }
            }

            return killedProcesses.Distinct().ToList();
        });
    }
}
