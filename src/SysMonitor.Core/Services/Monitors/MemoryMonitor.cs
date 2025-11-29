using System.Runtime.InteropServices;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

public class MemoryMonitor : IMemoryMonitor
{
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    public async Task<MemoryInfo> GetMemoryInfoAsync()
    {
        return await Task.Run(() =>
        {
            var info = new MemoryInfo();
            var memStatus = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX)) };

            if (GlobalMemoryStatusEx(ref memStatus))
            {
                info.TotalBytes = (long)memStatus.ullTotalPhys;
                info.AvailableBytes = (long)memStatus.ullAvailPhys;
                info.UsedBytes = info.TotalBytes - info.AvailableBytes;
                info.UsagePercent = memStatus.dwMemoryLoad;
                info.PageFileTotal = (long)memStatus.ullTotalPageFile;
                info.PageFileUsed = (long)(memStatus.ullTotalPageFile - memStatus.ullAvailPageFile);
            }

            return info;
        });
    }

    public async Task<double> GetUsagePercentAsync()
    {
        var info = await GetMemoryInfoAsync();
        return info.UsagePercent;
    }
}
