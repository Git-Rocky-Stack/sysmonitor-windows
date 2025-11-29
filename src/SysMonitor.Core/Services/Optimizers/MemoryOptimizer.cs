using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SysMonitor.Core.Services.Optimizers;

public class MemoryOptimizer : IMemoryOptimizer
{
    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr process, int minSize, int maxSize);

    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public async Task<long> OptimizeMemoryAsync()
    {
        return await Task.Run(() =>
        {
            long totalFreed = 0;
            var currentProcess = Process.GetCurrentProcess();

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
                    if (freed > 0) totalFreed += freed;
                }
                catch { }
            }

            // Force garbage collection
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

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
                return Math.Max(0, beforeMem - afterMem);
            }
            catch { return 0L; }
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
