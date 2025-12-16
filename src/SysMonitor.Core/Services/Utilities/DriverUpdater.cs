using System.Diagnostics;
using System.Management;
using System.Text;

namespace SysMonitor.Core.Services.Utilities;

public class DriverUpdater : IDriverUpdater
{
    // Device class icons mapping
    private static readonly Dictionary<string, string> DeviceClassIcons = new(StringComparer.OrdinalIgnoreCase)
    {
        { "Display", "\uE7F4" },           // Monitor icon
        { "Net", "\uE839" },               // Network icon
        { "Media", "\uE8D6" },             // Audio icon
        { "USB", "\uE88E" },               // USB icon
        { "Bluetooth", "\uE702" },         // Bluetooth icon
        { "Keyboard", "\uE765" },          // Keyboard icon
        { "Mouse", "\uE962" },             // Mouse icon
        { "Processor", "\uE950" },         // CPU icon
        { "System", "\uE770" },            // System icon
        { "DiskDrive", "\uEDA2" },         // Disk icon
        { "CDROM", "\uE958" },             // CD icon
        { "Monitor", "\uE7F4" },           // Monitor icon
        { "PrintQueue", "\uE749" },        // Printer icon
        { "Camera", "\uE722" },            // Camera icon
        { "Image", "\uE722" },             // Camera icon
        { "Battery", "\uE83F" },           // Battery icon
        { "HIDClass", "\uE961" },          // Input device
        { "SCSIAdapter", "\uEDA2" },       // Storage controller
        { "HDC", "\uEDA2" },               // Disk controller
        { "MTD", "\uE8B7" },               // Memory
        { "Biometric", "\uE8D7" },         // Fingerprint
        { "Sensor", "\uE957" },            // Sensor
    };

    // Critical device classes that matter most for system stability
    private static readonly HashSet<string> CriticalClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Display", "Net", "System", "Processor", "DiskDrive", "SCSIAdapter", "HDC", "USB"
    };

    public async Task<List<DriverInfo>> ScanDriversAsync(CancellationToken cancellationToken = default)
    {
        var drivers = new List<DriverInfo>();

        await Task.Run(() =>
        {
            try
            {
                // Query Win32_PnPSignedDriver for signed driver info
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL");

                foreach (ManagementObject obj in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var deviceName = obj["DeviceName"]?.ToString() ?? "";
                        if (string.IsNullOrWhiteSpace(deviceName))
                            continue;

                        var deviceClass = obj["DeviceClass"]?.ToString() ?? "";
                        var driverVersion = obj["DriverVersion"]?.ToString() ?? "";
                        var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                        var driverProvider = obj["DriverProviderName"]?.ToString() ?? "";
                        var infName = obj["InfName"]?.ToString() ?? "";
                        var isSigned = obj["IsSigned"] as bool? ?? false;
                        var deviceId = obj["DeviceID"]?.ToString() ?? "";

                        // Parse driver date
                        DateTime? driverDate = null;
                        var driverDateStr = obj["DriverDate"]?.ToString();
                        if (!string.IsNullOrEmpty(driverDateStr) && driverDateStr.Length >= 8)
                        {
                            // WMI date format: yyyyMMddHHmmss.ffffff+zzz
                            if (DateTime.TryParseExact(driverDateStr.Substring(0, 8), "yyyyMMdd",
                                null, System.Globalization.DateTimeStyles.None, out var parsedDate))
                            {
                                driverDate = parsedDate;
                            }
                        }

                        // Calculate days since update
                        int daysSinceUpdate = driverDate.HasValue
                            ? (int)(DateTime.Now - driverDate.Value).TotalDays
                            : 0;

                        // Check if outdated (older than 2 years)
                        bool isOutdated = daysSinceUpdate > 730;

                        // Get device class icon
                        var icon = GetDeviceClassIcon(deviceClass);

                        // Determine if critical
                        var isCritical = CriticalClasses.Contains(deviceClass);

                        drivers.Add(new DriverInfo
                        {
                            DeviceName = deviceName,
                            DeviceId = deviceId,
                            Manufacturer = manufacturer,
                            DriverVersion = driverVersion,
                            DriverDate = driverDate,
                            DriverProvider = driverProvider,
                            DeviceClass = deviceClass,
                            DeviceClassIcon = icon,
                            InfName = infName,
                            IsSigned = isSigned,
                            Status = "OK",
                            StatusColor = "#4CAF50",
                            HasProblem = false,
                            DaysSinceUpdate = daysSinceUpdate,
                            IsOutdated = isOutdated,
                            IsCritical = isCritical
                        });
                    }
                    catch { /* Skip problematic entries */ }
                }

                // Also check for problem devices
                var problemDrivers = GetProblemDevices();
                foreach (var problem in problemDrivers)
                {
                    // Update existing entry or add new
                    var existing = drivers.FirstOrDefault(d =>
                        d.DeviceName.Equals(problem.DeviceName, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        var index = drivers.IndexOf(existing);
                        drivers[index] = existing with
                        {
                            HasProblem = true,
                            Status = problem.Status,
                            StatusColor = problem.StatusColor,
                            ProblemDescription = problem.ProblemDescription
                        };
                    }
                    else
                    {
                        drivers.Add(problem);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DriverUpdater] Scan error: {ex.Message}");
            }
        }, cancellationToken);

        // Sort: problems first, then critical, then by name
        return drivers
            .OrderByDescending(d => d.HasProblem)
            .ThenByDescending(d => d.IsCritical)
            .ThenByDescending(d => d.IsOutdated)
            .ThenBy(d => d.DeviceName)
            .ToList();
    }

    private List<DriverInfo> GetProblemDevices()
    {
        var problems = new List<DriverInfo>();

        try
        {
            // Query Win32_PnPEntity for devices with problems
            // ConfigManagerErrorCode != 0 indicates a problem
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE ConfigManagerErrorCode != 0");

            foreach (ManagementObject obj in searcher.Get())
            {
                try
                {
                    var deviceName = obj["Name"]?.ToString() ?? obj["Caption"]?.ToString() ?? "Unknown Device";
                    var errorCode = Convert.ToInt32(obj["ConfigManagerErrorCode"] ?? 0);
                    var deviceClass = obj["PNPClass"]?.ToString() ?? "";
                    var manufacturer = obj["Manufacturer"]?.ToString() ?? "";
                    var deviceId = obj["DeviceID"]?.ToString() ?? "";

                    var (status, statusColor, description) = GetErrorDescription(errorCode);

                    problems.Add(new DriverInfo
                    {
                        DeviceName = deviceName,
                        DeviceId = deviceId,
                        Manufacturer = manufacturer,
                        DeviceClass = deviceClass,
                        DeviceClassIcon = GetDeviceClassIcon(deviceClass),
                        HasProblem = true,
                        Status = status,
                        StatusColor = statusColor,
                        ProblemDescription = description,
                        IsCritical = CriticalClasses.Contains(deviceClass)
                    });
                }
                catch { }
            }
        }
        catch { }

        return problems;
    }

    private static (string status, string color, string description) GetErrorDescription(int errorCode)
    {
        return errorCode switch
        {
            1 => ("Not Configured", "#FF9800", "Device is not configured correctly"),
            3 => ("Driver Corrupt", "#F44336", "Driver for this device might be corrupted"),
            10 => ("Cannot Start", "#F44336", "Device cannot start"),
            12 => ("Resource Conflict", "#FF9800", "Cannot find enough free resources"),
            14 => ("Restart Required", "#2196F3", "Device requires computer restart"),
            18 => ("Reinstall Drivers", "#FF9800", "Reinstall drivers for this device"),
            21 => ("Will Be Removed", "#FF9800", "Windows is removing this device"),
            22 => ("Disabled", "#808080", "Device is disabled"),
            24 => ("Not Present", "#808080", "Device is not present or not working"),
            28 => ("No Driver", "#F44336", "Drivers for this device are not installed"),
            29 => ("Disabled (BIOS)", "#808080", "Device is disabled in BIOS"),
            31 => ("Not Working", "#F44336", "Device is not working properly"),
            32 => ("Driver Blocked", "#F44336", "Driver for this device was blocked"),
            33 => ("IRQ Conflict", "#FF9800", "Cannot determine required resources"),
            34 => ("Need Manual Config", "#FF9800", "Device requires manual configuration"),
            35 => ("BIOS Memory Error", "#FF9800", "System BIOS doesn't have enough info"),
            36 => ("IRQ Conflict", "#FF9800", "Device requesting PCI interrupt"),
            37 => ("Cannot Initialize", "#F44336", "Cannot initialize device driver"),
            38 => ("Driver Load Failed", "#F44336", "Cannot load device driver"),
            39 => ("Driver Corrupt", "#F44336", "Driver corrupted or missing"),
            40 => ("Registry Error", "#F44336", "Registry entry is invalid"),
            41 => ("Driver Loaded", "#FF9800", "Driver loaded but device not found"),
            42 => ("Duplicate Device", "#FF9800", "Duplicate device detected"),
            43 => ("Driver Failed", "#F44336", "Driver reported device failure"),
            44 => ("Stopped", "#FF9800", "Device stopped by application or user"),
            45 => ("Disconnected", "#808080", "Device is not connected"),
            46 => ("Access Denied", "#F44336", "Windows cannot access device"),
            47 => ("Safe Removal", "#2196F3", "Device prepared for safe removal"),
            48 => ("Display Driver", "#FF9800", "Display driver blocked (known issues)"),
            49 => ("Registry Too Large", "#FF9800", "System hive too large"),
            50 => ("Cannot Apply", "#FF9800", "Cannot apply all settings"),
            51 => ("Unknown Problem", "#F44336", "Device has unknown problem"),
            52 => ("Unsigned Driver", "#FF9800", "Driver not digitally signed"),
            _ => ("Error", "#F44336", $"Unknown error code: {errorCode}")
        };
    }

    public async Task<List<DriverUpdate>> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var updates = new List<DriverUpdate>();

        // Note: Actually checking Windows Update for driver updates requires
        // the Windows Update Agent API which is complex. For now, we'll return
        // guidance to use Windows Update manually.

        await Task.Run(() =>
        {
            try
            {
                // Check if there are optional updates available via Windows Update
                // This is a simplified check - full WUA integration would be more complex
                System.Diagnostics.Debug.WriteLine("[DriverUpdater] Windows Update check would go here");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DriverUpdater] Update check error: {ex.Message}");
            }
        }, cancellationToken);

        return updates;
    }

    public async Task<List<DriverInfo>> GetProblemDriversAsync(CancellationToken cancellationToken = default)
    {
        var allDrivers = await ScanDriversAsync(cancellationToken);
        return allDrivers.Where(d => d.HasProblem || d.IsOutdated || !d.IsSigned).ToList();
    }

    public void OpenDeviceManager()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "devmgmt.msc",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DriverUpdater] Failed to open Device Manager: {ex.Message}");
        }
    }

    public void OpenWindowsUpdate()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:windowsupdate",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DriverUpdater] Failed to open Windows Update: {ex.Message}");
        }
    }

    public async Task<string> ExportDriverReportAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var drivers = await ScanDriversAsync(cancellationToken);
        var sb = new StringBuilder();

        sb.AppendLine("===========================================");
        sb.AppendLine("         DRIVER INVENTORY REPORT");
        sb.AppendLine($"         Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("===========================================");
        sb.AppendLine();

        // Summary
        sb.AppendLine("SUMMARY");
        sb.AppendLine("-------");
        sb.AppendLine($"Total Drivers: {drivers.Count}");
        sb.AppendLine($"Problem Drivers: {drivers.Count(d => d.HasProblem)}");
        sb.AppendLine($"Outdated Drivers (>2 years): {drivers.Count(d => d.IsOutdated)}");
        sb.AppendLine($"Unsigned Drivers: {drivers.Count(d => !d.IsSigned)}");
        sb.AppendLine();

        // Problem Drivers
        var problems = drivers.Where(d => d.HasProblem).ToList();
        if (problems.Count > 0)
        {
            sb.AppendLine("PROBLEM DRIVERS");
            sb.AppendLine("---------------");
            foreach (var driver in problems)
            {
                sb.AppendLine($"  [{driver.Status}] {driver.DeviceName}");
                sb.AppendLine($"    Problem: {driver.ProblemDescription}");
                sb.AppendLine();
            }
        }

        // All Drivers
        sb.AppendLine("ALL DRIVERS");
        sb.AppendLine("-----------");

        var grouped = drivers.GroupBy(d => d.DeviceClass).OrderBy(g => g.Key);
        foreach (var group in grouped)
        {
            sb.AppendLine();
            sb.AppendLine($"[{(string.IsNullOrEmpty(group.Key) ? "Other" : group.Key)}]");

            foreach (var driver in group.OrderBy(d => d.DeviceName))
            {
                sb.AppendLine($"  {driver.DeviceName}");
                sb.AppendLine($"    Version: {driver.DriverVersion}");
                sb.AppendLine($"    Date: {driver.DriverDate?.ToString("yyyy-MM-dd") ?? "Unknown"}");
                sb.AppendLine($"    Provider: {driver.DriverProvider}");
                sb.AppendLine($"    Signed: {(driver.IsSigned ? "Yes" : "NO")}");
                if (driver.HasProblem)
                    sb.AppendLine($"    Status: {driver.Status} - {driver.ProblemDescription}");
                sb.AppendLine();
            }
        }

        await File.WriteAllTextAsync(filePath, sb.ToString(), cancellationToken);
        return filePath;
    }

    private static string GetDeviceClassIcon(string deviceClass)
    {
        if (string.IsNullOrEmpty(deviceClass))
            return "\uE964"; // Default device icon

        // Try exact match first
        if (DeviceClassIcons.TryGetValue(deviceClass, out var icon))
            return icon;

        // Try partial match
        foreach (var kvp in DeviceClassIcons)
        {
            if (deviceClass.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                return kvp.Value;
        }

        return "\uE964"; // Default device icon
    }
}
