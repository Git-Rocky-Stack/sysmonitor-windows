using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class NetworkMapperViewModel : ObservableObject, IDisposable
{
    private readonly INetworkMapper _networkMapper;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _scanCts;
    private bool _isDisposed;

    public ObservableCollection<NetworkDeviceDisplay> Devices { get; } = [];

    // Local Network Info
    [ObservableProperty] private string _localIpAddress = "Checking...";
    [ObservableProperty] private string _subnetMask = "";
    [ObservableProperty] private string _gateway = "";
    [ObservableProperty] private string _macAddress = "";
    [ObservableProperty] private string _hostname = "";
    [ObservableProperty] private string _networkName = "";

    // Scan Settings
    [ObservableProperty] private string _scanSubnet = "";
    [ObservableProperty] private bool _autoDetectedSubnet = true;

    // Stats
    [ObservableProperty] private int _devicesFound;
    [ObservableProperty] private int _activeDevices;
    [ObservableProperty] private int _routersFound;
    [ObservableProperty] private int _computersFound;

    // Progress
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _scanProgress;
    [ObservableProperty] private string _scanStatus = "Ready to scan";
    [ObservableProperty] private string _currentTarget = "";

    // Selected Device
    [ObservableProperty] private NetworkDeviceDisplay? _selectedDevice;
    [ObservableProperty] private bool _hasSelectedDevice;

    // Port Scan
    [ObservableProperty] private bool _isPortScanning;
    [ObservableProperty] private string _portScanStatus = "";
    public ObservableCollection<PortDisplay> OpenPorts { get; } = [];

    // Action Status
    [ObservableProperty] private string _actionStatus = "";
    [ObservableProperty] private bool _hasActionStatus;
    [ObservableProperty] private string _actionStatusColor = "#4CAF50";

    public NetworkMapperViewModel(INetworkMapper networkMapper)
    {
        _networkMapper = networkMapper;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        await LoadLocalNetworkInfoAsync();
    }

    private async Task LoadLocalNetworkInfoAsync()
    {
        try
        {
            var info = await _networkMapper.GetLocalNetworkInfoAsync();

            _dispatcherQueue.TryEnqueue(() =>
            {
                LocalIpAddress = info.LocalIpAddress;
                SubnetMask = info.SubnetMask;
                Gateway = info.Gateway;
                MacAddress = info.MacAddress;
                Hostname = info.Hostname;
                NetworkName = info.NetworkName;

                // Auto-detect subnet for scanning
                if (AutoDetectedSubnet && !string.IsNullOrEmpty(info.LocalIpAddress))
                {
                    var parts = info.LocalIpAddress.Split('.');
                    if (parts.Length == 4)
                    {
                        ScanSubnet = $"{parts[0]}.{parts[1]}.{parts[2]}";
                    }
                }
            });
        }
        catch
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                LocalIpAddress = "Unable to detect";
                ScanSubnet = "192.168.1";
            });
        }
    }

    [RelayCommand]
    private async Task ScanNetworkAsync()
    {
        if (IsScanning)
        {
            _scanCts?.Cancel();
            return;
        }

        if (string.IsNullOrEmpty(ScanSubnet))
        {
            ShowAction("Please enter a subnet to scan (e.g., 192.168.1)", false);
            return;
        }

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanStatus = "Initializing scan...";
        Devices.Clear();
        ScanProgress = 0;

        try
        {
            var progress = new Progress<NetworkScanProgress>(p =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    ScanProgress = p.PercentComplete;
                    ScanStatus = p.Status;
                    CurrentTarget = p.CurrentTarget;

                    if (p.FoundDevice != null && !Devices.Any(d => d.IpAddress == p.FoundDevice.IpAddress))
                    {
                        Devices.Add(new NetworkDeviceDisplay(p.FoundDevice));
                        UpdateStats();
                    }
                });
            });

            var devices = await _networkMapper.ScanNetworkAsync(ScanSubnet, progress, _scanCts.Token);

            _dispatcherQueue.TryEnqueue(() =>
            {
                // Add any remaining devices not added via progress
                foreach (var device in devices)
                {
                    if (!Devices.Any(d => d.IpAddress == device.IpAddress))
                    {
                        Devices.Add(new NetworkDeviceDisplay(device));
                    }
                }

                UpdateStats();
                ScanStatus = $"Scan complete - {DevicesFound} devices found";
                ScanProgress = 100;
                ShowAction($"Found {DevicesFound} devices on {ScanSubnet}.x", true);
            });
        }
        catch (OperationCanceledException)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ScanStatus = "Scan cancelled";
                ShowAction("Network scan cancelled", false);
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ScanStatus = "Scan failed";
                ShowAction($"Error: {ex.Message}", false);
            });
        }
        finally
        {
            _dispatcherQueue.TryEnqueue(() => IsScanning = false);
        }
    }

    [RelayCommand]
    private void StopScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    private async Task ScanPortsAsync()
    {
        if (SelectedDevice == null)
        {
            ShowAction("Select a device first", false);
            return;
        }

        IsPortScanning = true;
        PortScanStatus = "Scanning common ports...";
        OpenPorts.Clear();

        try
        {
            int[] commonPorts = [20, 21, 22, 23, 25, 53, 80, 110, 143, 443, 445, 993, 995, 3306, 3389, 5432, 8080, 8443];
            var ports = await _networkMapper.ScanPortsAsync(SelectedDevice.IpAddress, commonPorts);

            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var port in ports.Where(p => p.IsOpen))
                {
                    OpenPorts.Add(new PortDisplay(port));
                }

                PortScanStatus = $"Found {OpenPorts.Count} open ports";
                ShowAction($"Port scan complete - {OpenPorts.Count} open ports", true);
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                PortScanStatus = "Port scan failed";
                ShowAction($"Error: {ex.Message}", false);
            });
        }
        finally
        {
            _dispatcherQueue.TryEnqueue(() => IsPortScanning = false);
        }
    }

    [RelayCommand]
    private void SelectDevice(NetworkDeviceDisplay? device)
    {
        SelectedDevice = device;
        HasSelectedDevice = device != null;
        OpenPorts.Clear();
        PortScanStatus = "";
    }

    [RelayCommand]
    private async Task RefreshDeviceAsync()
    {
        if (SelectedDevice == null) return;

        try
        {
            var info = await _networkMapper.GetDeviceInfoAsync(SelectedDevice.IpAddress);
            if (info != null)
            {
                var index = Devices.IndexOf(SelectedDevice);
                if (index >= 0)
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        var newDisplay = new NetworkDeviceDisplay(info);
                        Devices[index] = newDisplay;
                        SelectedDevice = newDisplay;
                        ShowAction("Device info refreshed", true);
                    });
                }
            }
        }
        catch (Exception ex)
        {
            ShowAction($"Refresh failed: {ex.Message}", false);
        }
    }

    [RelayCommand]
    private void CopyToClipboard(string text)
    {
        try
        {
            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            ShowAction("Copied to clipboard", true);
        }
        catch { }
    }

    private void UpdateStats()
    {
        DevicesFound = Devices.Count;
        ActiveDevices = Devices.Count(d => d.IsOnline);
        RoutersFound = Devices.Count(d => d.DeviceType == "Router" || d.DeviceType == "Gateway");
        ComputersFound = Devices.Count(d => d.DeviceType == "Computer" || d.DeviceType == "Server");
    }

    private void ShowAction(string message, bool isSuccess)
    {
        ActionStatus = message;
        ActionStatusColor = isSuccess ? "#4CAF50" : "#F44336";
        HasActionStatus = true;
        _ = ClearActionAfterDelayAsync();
    }

    private async Task ClearActionAfterDelayAsync()
    {
        await Task.Delay(5000);
        _dispatcherQueue.TryEnqueue(() => HasActionStatus = false);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
    }
}

public partial class NetworkDeviceDisplay : ObservableObject
{
    public string IpAddress { get; }
    public string Hostname { get; }
    public string MacAddress { get; }
    public string Manufacturer { get; }
    public string DeviceType { get; }
    public string DeviceIcon { get; }
    public string DeviceTypeColor { get; }
    public bool IsOnline { get; }
    public string StatusText { get; }
    public string StatusColor { get; }
    public int ResponseTime { get; }
    public string ResponseTimeText { get; }
    public DateTime LastSeen { get; }

    public NetworkDeviceDisplay(NetworkDeviceInfo info)
    {
        IpAddress = info.IpAddress;
        Hostname = string.IsNullOrEmpty(info.Hostname) ? "Unknown" : info.Hostname;
        MacAddress = info.MacAddress;
        Manufacturer = string.IsNullOrEmpty(info.Manufacturer) ? "Unknown" : info.Manufacturer;
        DeviceType = info.DeviceType;
        DeviceIcon = GetDeviceIcon(info.DeviceType);
        DeviceTypeColor = GetDeviceTypeColor(info.DeviceType);
        IsOnline = info.IsOnline;
        StatusText = info.IsOnline ? "Online" : "Offline";
        StatusColor = info.IsOnline ? "#4CAF50" : "#808080";
        ResponseTime = info.ResponseTimeMs;
        ResponseTimeText = info.ResponseTimeMs > 0 ? $"{info.ResponseTimeMs} ms" : "N/A";
        LastSeen = info.LastSeen;
    }

    private static string GetDeviceIcon(string type) => type switch
    {
        "Router" or "Gateway" => "\uE968",
        "Computer" => "\uE7F4",
        "Server" => "\uE977",
        "Printer" => "\uE749",
        "Phone" => "\uE8EA",
        "IoT" or "Smart Device" => "\uE957",
        "Camera" => "\uE722",
        "TV" or "Media" => "\uE7F4",
        _ => "\uE839"
    };

    private static string GetDeviceTypeColor(string type) => type switch
    {
        "Router" or "Gateway" => "#FF9800",
        "Computer" => "#2196F3",
        "Server" => "#9C27B0",
        "Printer" => "#607D8B",
        "Phone" => "#4CAF50",
        "IoT" or "Smart Device" => "#00BCD4",
        "Camera" => "#E91E63",
        "TV" or "Media" => "#673AB7",
        _ => "#808080"
    };
}

public class PortDisplay
{
    public int Port { get; }
    public string Service { get; }
    public string Protocol { get; }
    public string Status { get; }
    public string StatusColor { get; }

    public PortDisplay(PortInfo info)
    {
        Port = info.Port;
        Service = info.ServiceName;
        Protocol = info.Protocol;
        Status = info.IsOpen ? "Open" : "Closed";
        StatusColor = info.IsOpen ? "#4CAF50" : "#F44336";
    }
}
