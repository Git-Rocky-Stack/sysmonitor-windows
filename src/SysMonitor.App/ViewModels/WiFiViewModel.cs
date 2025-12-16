using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class WiFiViewModel : ObservableObject, IDisposable
{
    private readonly IWiFiAnalyzer _wifiAnalyzer;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _scanCts;
    private bool _isDisposed;

    public ObservableCollection<WiFiNetworkDisplay> Networks { get; } = [];

    // Adapter Info
    [ObservableProperty] private string _adapterName = "Checking...";
    [ObservableProperty] private string _adapterDescription = "";
    [ObservableProperty] private string _macAddress = "";
    [ObservableProperty] private bool _isAdapterEnabled;
    [ObservableProperty] private string _adapterStatus = "Unknown";
    [ObservableProperty] private string _adapterStatusColor = "#808080";

    // Current Connection
    [ObservableProperty] private string _connectedNetwork = "Not Connected";
    [ObservableProperty] private string _connectionSsid = "";
    [ObservableProperty] private string _connectionBssid = "";
    [ObservableProperty] private int _connectionSignal;
    [ObservableProperty] private string _connectionChannel = "";
    [ObservableProperty] private string _connectionSecurity = "";
    [ObservableProperty] private string _connectionSpeed = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionStatusColor = "#808080";

    // Stats
    [ObservableProperty] private int _networksFound;
    [ObservableProperty] private int _secureNetworks;
    [ObservableProperty] private int _openNetworks;
    [ObservableProperty] private int _networks24GHz;
    [ObservableProperty] private int _networks5GHz;

    // State
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private string _scanStatus = "Ready";

    // Permission warnings
    [ObservableProperty] private bool _hasPermissionError;
    [ObservableProperty] private string _permissionError = "";
    [ObservableProperty] private bool _requiresLocationPermission;
    [ObservableProperty] private bool _requiresAdminElevation;

    public WiFiViewModel(IWiFiAnalyzer wifiAnalyzer)
    {
        _wifiAnalyzer = wifiAnalyzer;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        IsAvailable = _wifiAnalyzer.IsAvailable;

        if (!IsAvailable)
        {
            AdapterName = "WiFi Not Available";
            AdapterStatus = "Not Found";
            AdapterStatusColor = "#F44336";
            return;
        }

        await LoadAdapterInfoAsync();
        await LoadCurrentConnectionAsync();
        await ScanNetworksAsync();
    }

    private async Task LoadAdapterInfoAsync()
    {
        var adapter = await _wifiAnalyzer.GetAdapterInfoAsync();

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (adapter != null)
            {
                AdapterName = adapter.Name;
                AdapterDescription = adapter.Description;
                MacAddress = adapter.MacAddress;
                IsAdapterEnabled = adapter.IsEnabled;
                AdapterStatus = adapter.Status;
                AdapterStatusColor = adapter.IsEnabled ? "#4CAF50" : "#F44336";
            }
            else
            {
                AdapterName = "WiFi Adapter";
                AdapterStatus = "Unknown";
                AdapterStatusColor = "#808080";
            }
        });
    }

    private async Task LoadCurrentConnectionAsync()
    {
        var connection = await _wifiAnalyzer.GetCurrentConnectionAsync();

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (connection != null && connection.IsConnected)
            {
                IsConnected = true;
                ConnectedNetwork = connection.Ssid;
                ConnectionSsid = connection.Ssid;
                ConnectionBssid = connection.Bssid;
                ConnectionSignal = connection.SignalStrength;
                ConnectionChannel = $"Channel {connection.Channel}";
                ConnectionSecurity = connection.Security;
                ConnectionSpeed = $"{connection.LinkSpeed} Mbps";
                ConnectionStatusColor = GetSignalColor(connection.SignalStrength);
            }
            else
            {
                IsConnected = false;
                ConnectedNetwork = "Not Connected";
                ConnectionStatusColor = "#808080";
            }
        });
    }

    [RelayCommand]
    private async Task ScanNetworksAsync()
    {
        if (IsScanning || !IsAvailable)
            return;

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanStatus = "Scanning for networks...";
        Networks.Clear();

        // Reset permission error state
        HasPermissionError = false;
        PermissionError = "";
        RequiresLocationPermission = false;
        RequiresAdminElevation = false;

        try
        {
            var networks = await _wifiAnalyzer.ScanNetworksAsync(_scanCts.Token);

            _dispatcherQueue.TryEnqueue(() =>
            {
                // Check for permission errors
                if (!string.IsNullOrEmpty(_wifiAnalyzer.PermissionError))
                {
                    HasPermissionError = true;
                    PermissionError = _wifiAnalyzer.PermissionError;
                    RequiresLocationPermission = _wifiAnalyzer.RequiresLocationPermission;
                    RequiresAdminElevation = _wifiAnalyzer.RequiresAdminElevation;
                    ScanStatus = "Permission required";
                }

                foreach (var network in networks.OrderByDescending(n => n.SignalStrength))
                {
                    Networks.Add(new WiFiNetworkDisplay(network));
                }

                NetworksFound = Networks.Count;
                SecureNetworks = Networks.Count(n => n.IsSecure);
                OpenNetworks = Networks.Count(n => !n.IsSecure);
                Networks24GHz = Networks.Count(n => n.Band == "2.4 GHz");
                Networks5GHz = Networks.Count(n => n.Band == "5 GHz");

                if (!HasPermissionError)
                {
                    ScanStatus = $"Found {NetworksFound} networks";
                }
            });
        }
        catch (OperationCanceledException)
        {
            _dispatcherQueue.TryEnqueue(() => ScanStatus = "Scan cancelled");
        }
        catch
        {
            _dispatcherQueue.TryEnqueue(() => ScanStatus = "Scan failed");
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
    private async Task RefreshConnectionAsync()
    {
        await LoadCurrentConnectionAsync();
    }

    private static string GetSignalColor(int signal)
    {
        return signal switch
        {
            >= 80 => "#4CAF50",
            >= 60 => "#8BC34A",
            >= 40 => "#FF9800",
            >= 20 => "#FF5722",
            _ => "#F44336"
        };
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
    }
}

public partial class WiFiNetworkDisplay : ObservableObject
{
    public string Ssid { get; }
    public string Bssid { get; }
    public int SignalStrength { get; }
    public string SignalText { get; }
    public string SignalColor { get; }
    public string SignalIcon { get; }
    public int Channel { get; }
    public string Band { get; }
    public string Frequency { get; }
    public string Security { get; }
    public bool IsSecure { get; }
    public string SecurityIcon { get; }
    public string SecurityColor { get; }
    public string NetworkType { get; }

    public WiFiNetworkDisplay(WiFiNetworkInfo info)
    {
        Ssid = string.IsNullOrEmpty(info.Ssid) ? "(Hidden Network)" : info.Ssid;
        Bssid = info.Bssid;
        SignalStrength = info.SignalStrength;
        SignalText = $"{info.SignalStrength}%";
        SignalColor = GetSignalColor(info.SignalStrength);
        SignalIcon = GetSignalIcon(info.SignalStrength);
        Channel = info.Channel;
        Band = info.Band;
        Frequency = $"{info.FrequencyMHz} MHz";
        Security = info.Security;
        IsSecure = !string.IsNullOrEmpty(info.Security) && info.Security != "Open";
        SecurityIcon = IsSecure ? "\uE72E" : "\uE785";
        SecurityColor = IsSecure ? "#4CAF50" : "#FF9800";
        NetworkType = info.NetworkType;
    }

    private static string GetSignalColor(int signal) => signal switch
    {
        >= 80 => "#4CAF50",
        >= 60 => "#8BC34A",
        >= 40 => "#FF9800",
        >= 20 => "#FF5722",
        _ => "#F44336"
    };

    private static string GetSignalIcon(int signal) => signal switch
    {
        >= 80 => "\uE871",
        >= 60 => "\uE870",
        >= 40 => "\uE86F",
        >= 20 => "\uE86E",
        _ => "\uE86D"
    };
}
