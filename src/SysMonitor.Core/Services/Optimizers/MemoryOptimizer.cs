using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace SysMonitor.Core.Services.Optimizers;

public class MemoryOptimizer : IMemoryOptimizer
{
    private readonly ILogger<MemoryOptimizer> _logger;

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, int minSize, int maxSize);

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
            var currentProcess = Process.GetCurrentProcess();
            var processesOptimized = 0;

            foreach (var proc in Process.GetProcesses())
            {
                if (proc.Id == currentProcess.Id) continue;
                try
                {
                    var beforeMem = proc.WorkingSet64;
                    EmptyWorkingSet(proc.Handle);
                    proc.Refresh();
                    var afterMem = proc.WorkingSet64;
                    var freed = beforeMem - afterMem;
                    if (freed > 0)
                    {
                        totalFreed += freed;
                        processesOptimized++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Failed to optimize memory for process {ProcessId}", proc.Id);
                }
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
                var proc = Process.GetProcessById(processId);
                var beforeMem = proc.WorkingSet64;
                EmptyWorkingSet(proc.Handle);
                proc.Refresh();
                var afterMem = proc.WorkingSet64;
                var freed = Math.Max(0, beforeMem - afterMem);
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

    public async Task<long> ClearStandbyListAsync()
    {
        return await Task.Run(() =>
        {
            // Requires admin privileges
            // This would use NtSetSystemInformation to clear standby list
            return 0L;
        });
    }
}
