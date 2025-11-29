namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Service for analyzing Bluetooth devices and signal
/// </summary>
public interface IBluetoothAnalyzer
{
    Task<List<BluetoothDeviceInfo>> ScanDevicesAsync(CancellationToken cancellationToken = default);
    Task<BluetoothAdapterInfo?> GetAdapterInfoAsync();
    bool IsAvailable { get; }
}

/// <summary>
/// Service for analyzing WiFi networks and signal
/// </summary>
public interface IWiFiAnalyzer
{
    Task<List<WiFiNetworkInfo>> ScanNetworksAsync(CancellationToken cancellationToken = default);
    Task<WiFiAdapterInfo?> GetAdapterInfoAsync();
    Task<WiFiConnectionInfo?> GetCurrentConnectionAsync();
    bool IsAvailable { get; }
}

// Bluetooth Models
public record BluetoothDeviceInfo
{
    public string Name { get; init; } = "Unknown Device";
    public string Address { get; init; } = "";
    public string DeviceType { get; init; } = "Unknown";
    public string DeviceTypeIcon { get; init; } = "\uE702";
    public int SignalStrength { get; init; } // RSSI in dBm
    public string SignalQuality { get; init; } = "Unknown";
    public string SignalColor { get; init; } = "#808080";
    public bool IsConnected { get; init; }
    public bool IsPaired { get; init; }
    public DateTime LastSeen { get; init; }
}

public record BluetoothAdapterInfo
{
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public bool IsEnabled { get; init; }
    public bool IsDiscoverable { get; init; }
    public string Status { get; init; } = "Unknown";
}

// WiFi Models
public record WiFiNetworkInfo
{
    public string SSID { get; init; } = "";
    public string BSSID { get; init; } = "";
    public int SignalStrength { get; init; } // Percentage 0-100
    public int SignalBars { get; init; } // 1-5 bars
    public string SignalQuality { get; init; } = "Unknown";
    public string SignalColor { get; init; } = "#808080";
    public int Channel { get; init; }
    public string Band { get; init; } = ""; // 2.4 GHz or 5 GHz
    public string SecurityType { get; init; } = "";
    public bool IsSecured { get; init; }
    public bool IsConnected { get; init; }
    public double FrequencyMHz { get; init; }
    public string NetworkType { get; init; } = ""; // 802.11n, 802.11ac, etc.
}

public record WiFiAdapterInfo
{
    public string Name { get; init; } = "";
    public string MacAddress { get; init; } = "";
    public bool IsEnabled { get; init; }
    public string Status { get; init; } = "";
}

public record WiFiConnectionInfo
{
    public string SSID { get; init; } = "";
    public int SignalStrength { get; init; }
    public string IpAddress { get; init; } = "";
    public double LinkSpeedMbps { get; init; }
    public string SecurityType { get; init; } = "";
    public TimeSpan ConnectionDuration { get; init; }
}
