using System.Management;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

public class DiskMonitor : IDiskMonitor
{
    public async Task<List<DiskInfo>> GetAllDisksAsync()
    {
        return await Task.Run(() =>
        {
            var disks = new List<DiskInfo>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (!drive.IsReady) continue;
                    disks.Add(new DiskInfo
                    {
                        Name = drive.Name,
                        Label = drive.VolumeLabel,
                        DriveType = drive.DriveType.ToString(),
                        FileSystem = drive.DriveFormat,
                        TotalBytes = drive.TotalSize,
                        FreeBytes = drive.AvailableFreeSpace,
                        UsedBytes = drive.TotalSize - drive.AvailableFreeSpace,
                        UsagePercent = (1 - (double)drive.AvailableFreeSpace / drive.TotalSize) * 100,
                        IsSSD = IsSSD(drive.Name)
                    });
                }
                catch { }
            }
            return disks;
        });
    }

    public async Task<DiskInfo?> GetDiskAsync(string driveLetter)
    {
        var disks = await GetAllDisksAsync();
        return disks.FirstOrDefault(d => d.Name.StartsWith(driveLetter, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<(double read, double write)> GetDiskSpeedAsync(string driveLetter)
    {
        return await Task.FromResult((0.0, 0.0));
    }

    private static bool IsSSD(string driveLetter)
    {
        try
        {
            string letter = driveLetter.TrimEnd(':', '\\');
            using var searcher = new ManagementObjectSearcher(
                $"SELECT MediaType FROM MSFT_PhysicalDisk WHERE DeviceId IN (SELECT DiskNumber FROM MSFT_Partition WHERE DriveLetter='{letter}')");
            searcher.Scope.Path = new ManagementPath(@"\\.\root\Microsoft\Windows\Storage");
            foreach (ManagementObject obj in searcher.Get())
            {
                int mediaType = Convert.ToInt32(obj["MediaType"]);
                return mediaType == 4;
            }
        }
        catch { }
        return false;
    }
}
