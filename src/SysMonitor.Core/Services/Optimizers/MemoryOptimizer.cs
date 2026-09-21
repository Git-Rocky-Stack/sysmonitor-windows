using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace SysMonitor.Core.Services.Optimizers;

public class MemoryOptimizer : IMemoryOptimizer
{
    private readonly ILogger<MemoryOptimizer> _logger;

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public MemoryOptimizer(ILogger<MemoryOptimizer> logger)
    {
        _logger = logger;
    }

    public async Task<long> OptimizeMemoryAsync()
    {
        return await Task.Run(() =>
        {
            long totalFreed = 0;
            var processesOptimized = 0;

            using var currentProcess = Process.GetCurrentProcess();
            var currentProcessId = currentProcess.Id;

            // Every Process here holds a kernel handle once .Handle is read, and nothing but Dispose gives
            // it back. Enumerating a few hundred processes and dropping them on the floor leaks a few
            // hundred handles per run; ProcessMonitor.GetAllProcessesAsync disposes for the same reason.
            var all = Process.GetProcesses();
            try
            {
                foreach (var proc in all)
                {
                    if (proc.Id == currentProcessId) continue;

                    try
                    {
                        var freed = Trim(proc);
                        if (freed > 0)
                        {
                            totalFreed += freed;
                            processesOptimized++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogTrace(ex, "Failed to optimize memory for process {ProcessId}", SafeId(proc));
                    }
                }
            }
            finally
            {
                foreach (var proc in all)
                    proc.Dispose();
            }

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            _logger.LogInformation("Memory optimization complete: freed {TotalFreed} bytes from {ProcessCount} processes",
                totalFreed, processesOptimized);
            return totalFreed;
        });
    }

    public async Task<long> TrimProcessWorkingSetAsync(int processId)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var proc = Process.GetProcessById(processId);
                var freed = Math.Max(0, Trim(proc));
                _logger.LogDebug("Trimmed {Freed} bytes from process {ProcessId}", freed, processId);
                return freed;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to trim working set for process {ProcessId}", processId);
                return 0L;
            }
        });
    }

    /// <summary>Pages out what the process is not using, and reports the drop in its working set.</summary>
    private static long Trim(Process process)
    {
        var before = process.WorkingSet64;
        EmptyWorkingSet(process.Handle);
        process.Refresh();
        return before - process.WorkingSet64;
    }

    /// <summary>The process id for a log line, when reading it may itself throw on an exited process.</summary>
    private static string SafeId(Process process)
    {
        try { return process.Id.ToString(); }
        catch { return "unknown"; }
    }
}
