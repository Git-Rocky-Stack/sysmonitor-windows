using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class BluetoothViewModel : ObservableObject, IDisposable
{
    private readonly IBluetoothAnalyzer _bluetoothAnalyzer;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _scanCts;
    private bool _isDisposed;

    public ObservableCollection<BluetoothDeviceInfo> Devices { get; } = [];

    // Adapter Info
    [ObservableProperty] private string _adapterName = "Checking...";
    [ObservableProperty] private string _adapterAddress = "";
    [ObservableProperty] private bool _isAdapterEnabled;
    [ObservableProperty] private string _adapterStatus = "Unknown";
    [ObservableProperty] private string _adapterStatusColor = "#808080";

    // Stats
    [ObservableProperty] private int _devicesFound;
    [ObservableProperty] private int _connectedDevices;
    [ObservableProperty] private int _pairedDevices;

    // State
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isAvailable;
    [ObservableProperty] private string _scanStatus = "Ready";

    public BluetoothViewModel(IBluetoothAnalyzer bluetoothAnalyzer)
    {
        _bluetoothAnalyzer = bluetoothAnalyzer;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        IsAvailable = _bluetoothAnalyzer.IsAvailable;

        if (!IsAvailable)
        {
            AdapterName = "Bluetooth Not Available";
            AdapterStatus = "Not Found";
            AdapterStatusColor = "#F44336";
            return;
        }

        await LoadAdapterInfoAsync();
        await ScanDevicesAsync();
    }

    private async Task LoadAdapterInfoAsync()
    {
        var adapter = await _bluetoothAnalyzer.GetAdapterInfoAsync();

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (adapter != null)
            {
                AdapterName = adapter.Name;
                AdapterAddress = adapter.Address;
                IsAdapterEnabled = adapter.IsEnabled;
                AdapterStatus = adapter.Status;
                AdapterStatusColor = adapter.IsEnabled ? "#4CAF50" : "#F44336";
            }
            else
            {
                AdapterName = "Bluetooth Adapter";
                AdapterStatus = "Unknown";
                AdapterStatusColor = "#808080";
            }
        });
    }

    [RelayCommand]
    private async Task ScanDevicesAsync()
    {
        if (IsScanning || !IsAvailable)
            return;

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanStatus = "Scanning for devices...";
        Devices.Clear();

        try
        {
            var devices = await _bluetoothAnalyzer.ScanDevicesAsync(_scanCts.Token);

            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var device in devices)
                {
                    Devices.Add(device);
                }

                DevicesFound = Devices.Count;
                ConnectedDevices = Devices.Count(d => d.IsConnected);
                PairedDevices = Devices.Count(d => d.IsPaired);
                ScanStatus = $"Found {DevicesFound} devices";
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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
    }
}
