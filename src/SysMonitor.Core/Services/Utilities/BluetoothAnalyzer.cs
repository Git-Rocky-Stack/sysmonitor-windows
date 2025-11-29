using System.Management;
using System.Net.NetworkInformation;

namespace SysMonitor.Core.Services.Utilities;

public class BluetoothAnalyzer : IBluetoothAnalyzer
{
    public bool IsAvailable => CheckBluetoothAvailable();

    public async Task<List<BluetoothDeviceInfo>> ScanDevicesAsync(CancellationToken cancellationToken = default)
    {
        var devices = new List<BluetoothDeviceInfo>();

        await Task.Run(() =>
        {
            try
            {
                // Query Bluetooth devices via WMI
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE Service='BTHUSB' OR Service='BthEnum' OR DeviceID LIKE 'BTHENUM%'");

                foreach (ManagementObject device in searcher.Get())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var name = device["Name"]?.ToString() ?? "Unknown Device";
                    var deviceId = device["DeviceID"]?.ToString() ?? "";
                    var status = device["Status"]?.ToString() ?? "Unknown";

                    // Skip the adapter itself
                    if (name.Contains("Bluetooth Adapter", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Bluetooth Radio", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var deviceType = DetermineDeviceType(name, deviceId);
                    var (quality, signalColor) = GetSignalQuality(-60); // Default signal strength
                    var isConnected = status == "OK";

                    devices.Add(new BluetoothDeviceInfo
                    {
                        Name = CleanDeviceName(name),
                        Address = ExtractAddress(deviceId),
                        DeviceType = deviceType.type,
                        DeviceIcon = deviceType.icon,
                        DeviceTypeColor = deviceType.color,
                        SignalStrength = -60, // WMI doesn't provide RSSI directly
                        SignalQuality = quality,
                        SignalColor = signalColor,
                        StatusColor = isConnected ? "#4CAF50" : "#808080",
                        IsConnected = isConnected,
                        IsPaired = true, // If visible in WMI, it's paired
                        LastSeen = DateTime.Now
                    });
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }, cancellationToken);

        return devices;
    }

    public async Task<BluetoothAdapterInfo?> GetAdapterInfoAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%Bluetooth%' AND (Service='BTHUSB' OR Service='bthport')");

                foreach (ManagementObject adapter in searcher.Get())
                {
                    var name = adapter["Name"]?.ToString() ?? "Bluetooth Adapter";
                    if (name.Contains("Radio", StringComparison.OrdinalIgnoreCase) ||
                        name.Contains("Adapter", StringComparison.OrdinalIgnoreCase))
                    {
                        var status = adapter["Status"]?.ToString() ?? "Unknown";
                        var deviceId = adapter["DeviceID"]?.ToString() ?? "";

                        return new BluetoothAdapterInfo
                        {
                            Name = name,
                            Address = ExtractAddress(deviceId),
                            IsEnabled = status == "OK",
                            IsDiscoverable = status == "OK",
                            Status = status == "OK" ? "Ready" : status
                        };
                    }
                }
            }
            catch { }

            return null;
        });
    }

    private static bool CheckBluetoothAvailable()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT * FROM Win32_PnPEntity WHERE Name LIKE '%Bluetooth%' AND Status='OK'");
            return searcher.Get().Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static (string type, string icon, string color) DetermineDeviceType(string name, string deviceId)
    {
        var nameLower = name.ToLowerInvariant();

        if (nameLower.Contains("headphone") || nameLower.Contains("headset") || nameLower.Contains("earphone") ||
            nameLower.Contains("airpod") || nameLower.Contains("earbud"))
            return ("Headphones", "\uE7F6", "#9C27B0");

        if (nameLower.Contains("speaker") || nameLower.Contains("audio"))
            return ("Speaker", "\uE7F5", "#E91E63");

        if (nameLower.Contains("keyboard"))
            return ("Keyboard", "\uE92E", "#2196F3");

        if (nameLower.Contains("mouse"))
            return ("Mouse", "\uE962", "#00BCD4");

        if (nameLower.Contains("gamepad") || nameLower.Contains("controller") || nameLower.Contains("xbox"))
            return ("Controller", "\uE7FC", "#4CAF50");

        if (nameLower.Contains("phone") || nameLower.Contains("iphone") || nameLower.Contains("samsung") ||
            nameLower.Contains("pixel") || nameLower.Contains("galaxy"))
            return ("Phone", "\uE8EA", "#FF9800");

        if (nameLower.Contains("watch") || nameLower.Contains("band") || nameLower.Contains("fitbit"))
            return ("Wearable", "\uE916", "#673AB7");

        if (nameLower.Contains("printer"))
            return ("Printer", "\uE749", "#607D8B");

        if (nameLower.Contains("laptop") || nameLower.Contains("computer") || nameLower.Contains("pc"))
            return ("Computer", "\uE7F8", "#3F51B5");

        return ("Device", "\uE702", "#808080");
    }

    private static string CleanDeviceName(string name)
    {
        // Remove common prefixes/suffixes
        var cleaned = name
            .Replace("Bluetooth Device", "")
            .Replace("Bluetooth", "")
            .Trim();

        return string.IsNullOrWhiteSpace(cleaned) ? name : cleaned;
    }

    private static string ExtractAddress(string deviceId)
    {
        // Try to extract MAC address from device ID (format varies)
        var parts = deviceId.Split('_', '\\', '&');
        foreach (var part in parts)
        {
            if (part.Length == 12 && part.All(c => char.IsLetterOrDigit(c)))
            {
                // Format as XX:XX:XX:XX:XX:XX
                return string.Join(":", Enumerable.Range(0, 6).Select(i => part.Substring(i * 2, 2)));
            }
        }
        return "";
    }

    private static (string quality, string color) GetSignalQuality(int rssi)
    {
        return rssi switch
        {
            >= -50 => ("Excellent", "#4CAF50"),
            >= -60 => ("Good", "#8BC34A"),
            >= -70 => ("Fair", "#FF9800"),
            >= -80 => ("Weak", "#FF5722"),
            _ => ("Very Weak", "#F44336")
        };
    }
}
