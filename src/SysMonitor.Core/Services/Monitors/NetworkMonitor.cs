using System.Net.NetworkInformation;
using System.Net.Sockets;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

public class NetworkMonitor : INetworkMonitor
{
    private long _lastBytesReceived;
    private long _lastBytesSent;
    private DateTime _lastMeasurement = DateTime.MinValue;

    public async Task<NetworkInfo> GetNetworkInfoAsync()
    {
        return await Task.Run(() =>
        {
            var info = new NetworkInfo();
            var activeInterface = GetActiveNetworkInterface();

            if (activeInterface != null)
            {
                info.IsConnected = true;
                info.AdapterName = activeInterface.Name;
                info.ConnectionType = activeInterface.NetworkInterfaceType.ToString();

                var stats = activeInterface.GetIPv4Statistics();
                info.BytesReceived = stats.BytesReceived;
                info.BytesSent = stats.BytesSent;

                var (upload, download) = CalculateSpeed(stats.BytesSent, stats.BytesReceived);
                info.UploadSpeedBps = upload;
                info.DownloadSpeedBps = download;

                var ipProps = activeInterface.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                info.IpAddress = ipv4?.Address.ToString() ?? string.Empty;
                info.MacAddress = BitConverter.ToString(activeInterface.GetPhysicalAddress().GetAddressBytes());
            }

            info.Adapters = GetAllAdapters();
            return info;
        });
    }

    private (double upload, double download) CalculateSpeed(long bytesSent, long bytesReceived)
    {
        var now = DateTime.Now;
        if (_lastMeasurement == DateTime.MinValue)
        {
            _lastBytesSent = bytesSent;
            _lastBytesReceived = bytesReceived;
            _lastMeasurement = now;
            return (0, 0);
        }

        var elapsed = (now - _lastMeasurement).TotalSeconds;
        if (elapsed < 0.1) return (0, 0);

        var upload = (bytesSent - _lastBytesSent) / elapsed;
        var download = (bytesReceived - _lastBytesReceived) / elapsed;

        _lastBytesSent = bytesSent;
        _lastBytesReceived = bytesReceived;
        _lastMeasurement = now;

        return (Math.Max(0, upload), Math.Max(0, download));
    }

    public async Task<List<NetworkAdapter>> GetAdaptersAsync()
    {
        return await Task.Run(() => GetAllAdapters());
    }

    private static List<NetworkAdapter> GetAllAdapters()
    {
        var adapters = new List<NetworkAdapter>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            try
            {
                var ipProps = ni.GetIPProperties();
                var ipv4 = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);

                adapters.Add(new NetworkAdapter
                {
                    Name = ni.Name,
                    Description = ni.Description,
                    Type = ni.NetworkInterfaceType.ToString(),
                    Status = ni.OperationalStatus.ToString(),
                    IpAddress = ipv4?.Address.ToString() ?? string.Empty,
                    MacAddress = BitConverter.ToString(ni.GetPhysicalAddress().GetAddressBytes()),
                    Speed = ni.Speed
                });
            }
            catch { }
        }
        return adapters;
    }

    private static NetworkInterface? GetActiveNetworkInterface()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback
                && ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
            .OrderByDescending(ni => ni.Speed)
            .FirstOrDefault();
    }

    public async Task<(double upload, double download)> GetSpeedAsync()
    {
        var info = await GetNetworkInfoAsync();
        return (info.UploadSpeedBps, info.DownloadSpeedBps);
    }
}
