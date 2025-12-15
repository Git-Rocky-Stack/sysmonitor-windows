using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace SysMonitor.Core.Services.Utilities;

public interface ISystemRestoreService
{
    Task<RestorePointResult> CreateRestorePointAsync(string description, RestorePointType type = RestorePointType.ApplicationInstall);
    Task<List<RestorePointInfo>> GetRestorePointsAsync();
    Task<bool> IsSystemRestoreEnabledAsync();
    Task<bool> EnableSystemRestoreAsync(string driveLetter = "C:");
}

public enum RestorePointType
{
    ApplicationInstall = 0,
    ApplicationUninstall = 1,
    DeviceDriverInstall = 10,
    ModifySettings = 12,
    CancelledOperation = 13
}

public class RestorePointInfo
{
    public uint SequenceNumber { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime CreationTime { get; set; }
    public RestorePointType Type { get; set; }
}

public class RestorePointResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public uint? SequenceNumber { get; set; }
    public TimeSpan Duration { get; set; }
}

[SupportedOSPlatform("windows")]
public class SystemRestoreService : ISystemRestoreService
{
    public async Task<RestorePointResult> CreateRestorePointAsync(string description,
        RestorePointType type = RestorePointType.ApplicationInstall)
    {
        var result = new RestorePointResult();
        var startTime = DateTime.Now;

        try
        {
            await Task.Run(() =>
            {
                // Use WMI to create restore point
                using var restorePointClass = new ManagementClass("\\\\.\\root\\default", "SystemRestore", new ObjectGetOptions());

                using var inParams = restorePointClass.GetMethodParameters("CreateRestorePoint");
                inParams["Description"] = description;
                inParams["RestorePointType"] = (uint)type;
                inParams["EventType"] = 100u; // BEGIN_SYSTEM_CHANGE

                using var outParams = restorePointClass.InvokeMethod("CreateRestorePoint", inParams, null);

                var returnValue = Convert.ToUInt32(outParams["ReturnValue"]);

                if (returnValue == 0)
                {
                    result.Success = true;
                    result.Message = $"Restore point '{description}' created successfully.";
                }
                else
                {
                    result.Success = false;
                    result.Message = GetErrorMessage(returnValue);
                }
            });
        }
        catch (UnauthorizedAccessException)
        {
            result.Success = false;
            result.Message = "Administrator privileges required to create restore points.";
        }
        catch (ManagementException ex)
        {
            result.Success = false;
            result.Message = $"System Restore error: {ex.Message}";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"Failed to create restore point: {ex.Message}";
        }

        result.Duration = DateTime.Now - startTime;
        return result;
    }

    public async Task<List<RestorePointInfo>> GetRestorePointsAsync()
    {
        var restorePoints = new List<RestorePointInfo>();

        try
        {
            await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher("root\\default",
                    "SELECT * FROM SystemRestore ORDER BY CreationTime DESC");

                foreach (ManagementObject queryObj in searcher.Get())
                {
                    try
                    {
                        var creationTimeStr = queryObj["CreationTime"]?.ToString();
                        DateTime creationTime = DateTime.MinValue;

                        if (!string.IsNullOrEmpty(creationTimeStr))
                        {
                            // WMI datetime format: yyyyMMddHHmmss.ffffff+UUU
                            creationTime = ManagementDateTimeConverter.ToDateTime(creationTimeStr);
                        }

                        restorePoints.Add(new RestorePointInfo
                        {
                            SequenceNumber = Convert.ToUInt32(queryObj["SequenceNumber"] ?? 0),
                            Description = queryObj["Description"]?.ToString() ?? "Unknown",
                            CreationTime = creationTime,
                            Type = (RestorePointType)Convert.ToInt32(queryObj["RestorePointType"] ?? 0)
                        });
                    }
                    catch { }
                }
            });
        }
        catch { }

        return restorePoints;
    }

    public async Task<bool> IsSystemRestoreEnabledAsync()
    {
        try
        {
            return await Task.Run(() =>
            {
                using var searcher = new ManagementObjectSearcher("root\\default",
                    "SELECT * FROM SystemRestoreConfig WHERE DriveLetter = 'C:\\\\'");

                foreach (ManagementObject queryObj in searcher.Get())
                {
                    var rPSessionInterval = queryObj["RPSessionInterval"];
                    // If RPSessionInterval is 0 or doesn't exist, restore is enabled
                    return rPSessionInterval == null || Convert.ToUInt32(rPSessionInterval) != 1;
                }

                return false;
            });
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> EnableSystemRestoreAsync(string driveLetter = "C:")
    {
        try
        {
            return await Task.Run(() =>
            {
                driveLetter = driveLetter.TrimEnd('\\', ':') + ":\\";

                using var restoreConfigClass = new ManagementClass("\\\\.\\root\\default", "SystemRestore", new ObjectGetOptions());
                using var inParams = restoreConfigClass.GetMethodParameters("Enable");
                inParams["Drive"] = driveLetter;

                using var outParams = restoreConfigClass.InvokeMethod("Enable", inParams, null);
                var returnValue = Convert.ToUInt32(outParams["ReturnValue"]);

                return returnValue == 0;
            });
        }
        catch
        {
            return false;
        }
    }

    private static string GetErrorMessage(uint errorCode) => errorCode switch
    {
        0 => "Success",
        1 => "Access denied. Administrator privileges required.",
        2 => "System restore is disabled.",
        3 => "Insufficient disk space.",
        4 => "Invalid drive letter.",
        87 => "Invalid parameter.",
        1058 => "System restore service is disabled.",
        1060 => "System restore service not found.",
        0x80070005 => "Access denied. Run as administrator.",
        _ => $"Unknown error (code: {errorCode})"
    };
}
