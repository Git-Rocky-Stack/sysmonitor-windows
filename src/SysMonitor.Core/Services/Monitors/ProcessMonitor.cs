using System.Diagnostics;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

public class ProcessMonitor : IProcessMonitor
{
    private readonly Dictionary<int, (DateTime time, TimeSpan cpu)> _cpuUsageCache = new();

    public async Task<List<ProcessInfo>> GetAllProcessesAsync()
    {
        return await Task.Run(() =>
        {
            var processes = new List<ProcessInfo>();
            var systemProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "System", "Idle", "smss", "csrss", "wininit", "services", "lsass",
                "svchost", "dwm", "winlogon", "fontdrvhost", "Registry"
            };

            foreach (var proc in Process.GetProcesses())
            {
                try
                {
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
            }

            return processes.OrderByDescending(p => p.MemoryBytes).ToList();
        });
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
                var proc = Process.GetProcessById(processId);
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
                var proc = Process.GetProcessById(processId);
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
