using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Monitors;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class NetworkViewModel : ObservableObject, IDisposable
{
    private readonly INetworkMonitor _networkMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;

    // Connection Status
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _connectionStatus = "Checking...";
    [ObservableProperty] private string _connectionType = "";
    [ObservableProperty] private string _adapterName = "";

    // Network Addresses
    [ObservableProperty] private string _ipAddress = "";
    [ObservableProperty] private string _macAddress = "";

    // Speed Stats
    [ObservableProperty] private double _downloadSpeedBps;
    [ObservableProperty] private double _uploadSpeedBps;
    [ObservableProperty] private string _downloadSpeedDisplay = "0 B/s";
    [ObservableProperty] private string _uploadSpeedDisplay = "0 B/s";

    // Data Transferred
    [ObservableProperty] private long _bytesReceived;
    [ObservableProperty] private long _bytesSent;
    [ObservableProperty] private string _totalReceivedDisplay = "0 B";
    [ObservableProperty] private string _totalSentDisplay = "0 B";

    // Adapters
    [ObservableProperty] private ObservableCollection<NetworkAdapter> _adapters = new();

    // State
    [ObservableProperty] private bool _isLoading = true;

    public NetworkViewModel(INetworkMonitor networkMonitor)
    {
        _networkMonitor = networkMonitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        await RefreshDataAsync();
        StartAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _cts = new CancellationTokenSource();
        _ = RefreshLoopAsync(_cts.Token);
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshDataAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disposed
        }
    }

    private async Task RefreshDataAsync()
    {
        if (_isDisposed) return;

        try
        {
            var netInfo = await _networkMonitor.GetNetworkInfoAsync();
            if (_isDisposed) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                // Connection Status
                IsConnected = netInfo.IsConnected;
                ConnectionStatus = netInfo.IsConnected ? "Connected" : "Disconnected";
                ConnectionType = FormatConnectionType(netInfo.ConnectionType);
                AdapterName = netInfo.AdapterName;

                // Addresses
                IpAddress = netInfo.IpAddress;
                MacAddress = netInfo.MacAddress;

                // Speed
                DownloadSpeedBps = netInfo.DownloadSpeedBps;
                UploadSpeedBps = netInfo.UploadSpeedBps;
                DownloadSpeedDisplay = FormatSpeed(netInfo.DownloadSpeedBps);
                UploadSpeedDisplay = FormatSpeed(netInfo.UploadSpeedBps);

                // Data Transferred
                BytesReceived = netInfo.BytesReceived;
                BytesSent = netInfo.BytesSent;
                TotalReceivedDisplay = FormatBytes(netInfo.BytesReceived);
                TotalSentDisplay = FormatBytes(netInfo.BytesSent);

                // Update adapters (only on first load or if count changed)
                if (Adapters.Count != netInfo.Adapters.Count)
                {
                    Adapters.Clear();
                    foreach (var adapter in netInfo.Adapters)
                    {
                        Adapters.Add(adapter);
                    }
                }

                IsLoading = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception)
        {
            // Log in production
        }
    }

    private static string FormatConnectionType(string type)
    {
        return type switch
        {
            "Ethernet" => "Ethernet",
            "Wireless80211" => "Wi-Fi",
            "GigabitEthernet" => "Gigabit Ethernet",
            _ => type
        };
    }

    private static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1_000_000_000)
            return $"{bytesPerSecond / 1_000_000_000:F2} GB/s";
        if (bytesPerSecond >= 1_000_000)
            return $"{bytesPerSecond / 1_000_000:F2} MB/s";
        if (bytesPerSecond >= 1_000)
            return $"{bytesPerSecond / 1_000:F2} KB/s";
        return $"{bytesPerSecond:F0} B/s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1_000_000_000_000)
            return $"{bytes / 1_000_000_000_000.0:F2} TB";
        if (bytes >= 1_000_000_000)
            return $"{bytes / 1_000_000_000.0:F2} GB";
        if (bytes >= 1_000_000)
            return $"{bytes / 1_000_000.0:F2} MB";
        if (bytes >= 1_000)
            return $"{bytes / 1_000.0:F2} KB";
        return $"{bytes} B";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
