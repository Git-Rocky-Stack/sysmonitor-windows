using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Models;
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
    [ObservableProperty] private LampState _adapterStatusState = LampState.Off;

    // Current Connection
    [ObservableProperty] private string _connectedNetwork = "Not Connected";
    [ObservableProperty] private string _connectionSsid = "";
    [ObservableProperty] private string _connectionBssid = "";
    [ObservableProperty] private int _connectionSignal;
    [ObservableProperty] private string _connectionChannel = "";
    [ObservableProperty] private string _connectionSecurity = "";
    [ObservableProperty] private string _connectionSpeed = "";
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private LampState _connectionStatusState = LampState.Off;

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
            AdapterStatusState = LampState.NoGo;
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
                AdapterStatusState = adapter.IsEnabled ? LampState.Go : LampState.NoGo;
            }
            else
            {
                AdapterName = "WiFi Adapter";
                AdapterStatus = "Unknown";
                AdapterStatusState = LampState.Off;
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
                ConnectionStatusState = WiFiNetworkDisplay.GetSignalState(connection.SignalStrength);
            }
            else
            {
                IsConnected = false;
                ConnectedNetwork = "Not Connected";
                ConnectionStatusState = LampState.Off;
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
                // A network whose security could not be read is neither secure nor open, and counting it
                // as open would overstate how much of the air around the user is unencrypted.
                OpenNetworks = Networks.Count(n => n.IsSecurityKnown && !n.IsSecure);
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
    public LampState SignalState { get; }
    public string SignalIcon { get; }
    public int Channel { get; }
    public string Band { get; }
    public string Frequency { get; }
    public string Security { get; }
    public bool IsSecure { get; }

    /// <summary>False when the platform would not say whether the network is encrypted.</summary>
    public bool IsSecurityKnown { get; }
    public string SecurityIcon { get; }
    public LampState SecurityState { get; }
    public string NetworkType { get; }

    public WiFiNetworkDisplay(WiFiNetworkInfo info)
    {
        Ssid = string.IsNullOrEmpty(info.Ssid) ? "(Hidden Network)" : info.Ssid;
        Bssid = info.Bssid;
        SignalStrength = info.SignalStrength;
        SignalText = $"{info.SignalStrength}%";
        SignalState = GetSignalState(info.SignalStrength);
        SignalIcon = GetSignalIcon(info.SignalStrength);
        Channel = info.Channel;
        Band = info.Band;
        Frequency = $"{info.FrequencyMHz} MHz";
        // Three answers, not two: encrypted, not encrypted, and not known. Treating "not known" as
        // encrypted put a green padlock on networks whose security the app had never read.
        var state = WiFiSecurity.Describe(info.Security);
        Security = state == WiFiSecurityState.Unknown ? "Security unknown" : info.Security;
        IsSecure = state == WiFiSecurityState.Secured;
        IsSecurityKnown = state != WiFiSecurityState.Unknown;
        SecurityIcon = state switch
        {
            WiFiSecurityState.Secured => "\uE72E",   // closed padlock
            WiFiSecurityState.Open => "\uE785",      // open padlock
            _ => "\uE9CE",                           // question mark
        };
        SecurityState = state switch
        {
            WiFiSecurityState.Secured => LampState.Go,
            WiFiSecurityState.Open => LampState.Hold,
            _ => LampState.Off,
        };
        NetworkType = info.NetworkType;
    }

    /// <summary>The lamp for a signal strength in percent; the view model's connection lamp uses it too.</summary>
    internal static LampState GetSignalState(int signal) => signal switch
    {
        >= 80 => LampState.Go,
        >= 60 => LampState.Go,
        >= 40 => LampState.Hold,
        >= 20 => LampState.Warn,
        _ => LampState.NoGo
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
