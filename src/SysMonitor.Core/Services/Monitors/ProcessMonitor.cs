using System.Diagnostics;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

public class ProcessMonitor : IProcessMonitor
{
    private readonly Dictionary<int, (DateTime time, TimeSpan cpu)> _cpuUsageCache = new();
    private DateTime _lastCacheCleanup = DateTime.UtcNow;
    private readonly TimeSpan _cacheCleanupInterval = TimeSpan.FromSeconds(15);
    private const int MaxCacheSize = 500;

    public async Task<List<ProcessInfo>> GetAllProcessesAsync()
    {
        return await Task.Run(() =>
        {
            var processes = new List<ProcessInfo>();
            var currentProcessIds = new HashSet<int>();
            var systemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "Idle", "smss", "csrss", "wininit", "services", "lsass",
                "svchost", "dwm", "winlogon", "fontdrvhost", "Registry"
            };

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    currentProcessIds.Add(proc.Id);

                    var info = new ProcessInfo
                    {
                        Id = proc.Id,
                        Name = proc.ProcessName,
                        Status = proc.Responding ? ProcessStatus.Running : ProcessStatus.NotResponding,
                        MemoryBytes = proc.WorkingSet64,
                        WorkingSetBytes = proc.WorkingSet64,
                        PrivateMemoryBytes = proc.PrivateMemorySize64,
                        ThreadCount = proc.Threads.Count,
                        HandleCount = proc.HandleCount,
                        IsSystemProcess = systemProcesses.Contains(proc.ProcessName),
                        Priority = MapPriority(proc.BasePriority),
                        CpuUsagePercent = CalculateCpuUsage(proc)
                    };

                    try
                    {
                        info.StartTime = proc.StartTime;
                        info.TotalProcessorTime = proc.TotalProcessorTime;
                    }
                    catch { }

                    try
                    {
                        info.FilePath = proc.MainModule?.FileName ?? string.Empty;
                    }
                    catch { }

                    processes.Add(info);
                }
                catch { }
                finally
                {
                    proc.Dispose();
                }
            }

            // Clean up CPU cache for dead processes periodically
            CleanupCpuCache(currentProcessIds);

            return processes.OrderByDescending(p => p.MemoryBytes).ToList();
        });
    }

    private void CleanupCpuCache(HashSet<int> currentProcessIds)
    {
        var now = DateTime.UtcNow;

        // Force cleanup if cache is too large
        var forceCleanup = _cpuUsageCache.Count > MaxCacheSize;

        if (!forceCleanup && now - _lastCacheCleanup < _cacheCleanupInterval) return;

        _lastCacheCleanup = now;

        // Remove entries for dead processes
        var deadProcessIds = _cpuUsageCache.Keys.Where(id => !currentProcessIds.Contains(id)).ToList();
        foreach (var id in deadProcessIds)
        {
            _cpuUsageCache.Remove(id);
        }

        // If still over limit, remove oldest entries
        if (_cpuUsageCache.Count > MaxCacheSize)
        {
            var toRemove = _cpuUsageCache
                .OrderBy(kvp => kvp.Value.time)
                .Take(_cpuUsageCache.Count - MaxCacheSize)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (var id in toRemove)
            {
                _cpuUsageCache.Remove(id);
            }
        }
    }

    private double CalculateCpuUsage(Process proc)
    {
        try
        {
            var now = DateTime.UtcNow;
            var currentCpu = proc.TotalProcessorTime;

            if (_cpuUsageCache.TryGetValue(proc.Id, out var cached))
            {
                var elapsed = (now - cached.time).TotalMilliseconds;
                if (elapsed > 100)
                {
                    var cpuDiff = (currentCpu - cached.cpu).TotalMilliseconds;
                    var usage = (cpuDiff / elapsed) / Environment.ProcessorCount * 100;
                    _cpuUsageCache[proc.Id] = (now, currentCpu);
                    return Math.Min(100, Math.Max(0, usage));
                }
            }
            else
            {
                _cpuUsageCache[proc.Id] = (now, currentCpu);
            }
        }
        catch { }
        return 0;
    }

    public async Task<ProcessInfo?> GetProcessAsync(int processId)
    {
        var processes = await GetAllProcessesAsync();
        return processes.FirstOrDefault(p => p.Id == processId);
    }

    public async Task<bool> KillProcessAsync(int processId)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var proc = Process.GetProcessById(processId);
                proc.Kill();
                return proc.WaitForExit(5000);
            }
            catch { return false; }
        });
    }

    public async Task<bool> SetPriorityAsync(int processId, ProcessPriority priority)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var proc = Process.GetProcessById(processId);
                proc.PriorityClass = priority switch
                {
                    ProcessPriority.Idle => ProcessPriorityClass.Idle,
                    ProcessPriority.BelowNormal => ProcessPriorityClass.BelowNormal,
                    ProcessPriority.Normal => ProcessPriorityClass.Normal,
                    ProcessPriority.AboveNormal => ProcessPriorityClass.AboveNormal,
                    ProcessPriority.High => ProcessPriorityClass.High,
                    ProcessPriority.RealTime => ProcessPriorityClass.RealTime,
                    _ => ProcessPriorityClass.Normal
                };
                return true;
            }
            catch { return false; }
        });
    }

    private static ProcessPriority MapPriority(int basePriority)
    {
        return basePriority switch
        {
            <= 4 => ProcessPriority.Idle,
            <= 6 => ProcessPriority.BelowNormal,
            <= 8 => ProcessPriority.Normal,
            <= 10 => ProcessPriority.AboveNormal,
            <= 13 => ProcessPriority.High,
            _ => ProcessPriority.RealTime
        };
    }
}
