using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

public class NetworkMonitor : INetworkMonitor
{
    private readonly ILogger _logger;

    public NetworkMonitor(ILogger<NetworkMonitor>? logger = null)
    {
        _logger = logger ?? NullLogger<NetworkMonitor>.Instance;
    }

    private readonly NetworkSpeedSampler _sampler = new();

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

                // Everything the adapter carried, not only IPv4: a machine on IPv6 showed nothing moving.
                var stats = activeInterface.GetIPStatistics();
                info.BytesReceived = stats.BytesReceived;
                info.BytesSent = stats.BytesSent;

                var (upload, download) = _sampler.Sample(activeInterface.Id, stats.BytesSent, stats.BytesReceived, DateTime.UtcNow);
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

    public async Task<List<NetworkAdapter>> GetAdaptersAsync()
    {
        return await Task.Run(() => GetAllAdapters());
    }

    public async Task<(double upload, double download)> GetSpeedAsync()
    {
        return await Task.Run(() =>
        {
            var activeInterface = GetActiveNetworkInterface();
            if (activeInterface == null)
            {
                return (0d, 0d);
            }

            var stats = activeInterface.GetIPStatistics();
            return _sampler.Sample(activeInterface.Id, stats.BytesSent, stats.BytesReceived, DateTime.UtcNow);
        });
    }

    private List<NetworkAdapter> GetAllAdapters()
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
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetAllAdapters failed");
            }
        }
        return adapters;
    }

    private static NetworkInterface? GetActiveNetworkInterface()
    {
        var interfaces = NetworkInterface.GetAllNetworkInterfaces();
        var routedIndex = RoutedInterfaceIndex();

        var chosen = ChooseActive(interfaces.Select(ni => Describe(ni, routedIndex)));
        return chosen == null ? null : interfaces.FirstOrDefault(ni => ni.Id == chosen.Value.Id);
    }

    /// <summary>What matters about an adapter when picking the one the machine actually uses.</summary>
    internal readonly record struct AdapterChoice(
        string Id, bool IsUp, bool IsLoopbackOrTunnel, bool OwnsDefaultRoute, bool HasDefaultGateway, long Speed);

    /// <summary>
    /// The adapter to report on. Picking the fastest link picks a Hyper-V, WSL or Docker switch, which claims
    /// 10 Gb/s and carries nothing - that is what showed 0 B/s, and the wrong name, address and MAC with it.
    /// What counts is carrying the traffic: the interface Windows routes through, then one with a default
    /// gateway, and only then link speed.
    /// </summary>
    internal static AdapterChoice? ChooseActive(IEnumerable<AdapterChoice> adapters)
    {
        var usable = adapters
            .Where(a => a is { IsUp: true, IsLoopbackOrTunnel: false })
            .OrderByDescending(a => a.OwnsDefaultRoute)
            .ThenByDescending(a => a.HasDefaultGateway)
            .ThenByDescending(a => a.Speed)
            .ToList();

        return usable.Count == 0 ? null : usable[0];
    }

    private static AdapterChoice Describe(NetworkInterface ni, uint? routedIndex)
    {
        var isUp = ni.OperationalStatus == OperationalStatus.Up;
        var isLoopbackOrTunnel = ni.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel;
        var hasGateway = false;
        var ownsDefaultRoute = false;

        try
        {
            var properties = ni.GetIPProperties();
            hasGateway = properties.GatewayAddresses.Any(gateway =>
                gateway.Address is { } address && !address.Equals(System.Net.IPAddress.Any) && !address.Equals(System.Net.IPAddress.IPv6Any));

            if (routedIndex is { } index)
            {
                try
                {
                    ownsDefaultRoute = properties.GetIPv4Properties()?.Index == (int)index;
                }
                catch (NetworkInformationException)
                {
                    // Best effort: the adapter has no IPv4 properties; the gateway test above still applies.
                }
            }
        }
        catch (NetworkInformationException)
        {
            // Best effort: an adapter that will not describe itself cannot be the one carrying the
            // traffic this is looking for.
        }

        return new AdapterChoice(ni.Id, isUp, isLoopbackOrTunnel, ownsDefaultRoute, hasGateway, ni.Speed);
    }

    /// <summary>The interface Windows would route internet traffic through, or null when it cannot say.</summary>
    private static uint? RoutedInterfaceIndex()
    {
        try
        {
            // 8.8.8.8 in network byte order. Any routable address answers the same question.
            const uint destination = 8u | (8u << 8) | (8u << 16) | (8u << 24);
            return GetBestInterface(destination, out var index) == 0 ? index : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int GetBestInterface(uint destinationAddress, out uint interfaceIndex);
}

/// <summary>
/// Turns an adapter's ever-growing byte counters into a speed. Several pages ask during the same refresh, so
/// the answer is kept and given again to whoever asks within the same tick: the old code handed the second
/// caller a zero, and that zero is what reached the dashboard.
/// </summary>
internal sealed class NetworkSpeedSampler
{
    /// <summary>The shortest gap between two readings that still says anything about the rate.</summary>
    internal static readonly TimeSpan MinimumInterval = TimeSpan.FromMilliseconds(250);

    private readonly object _gate = new();
    private string _adapterId = string.Empty;
    private long _bytesSent;
    private long _bytesReceived;
    private DateTime _measuredAt;
    private (double Upload, double Download) _last;

    public (double Upload, double Download) Sample(string adapterId, long bytesSent, long bytesReceived, DateTime now)
    {
        lock (_gate)
        {
            // Another adapter's counters say nothing about this one, and neither does a first reading.
            if (_measuredAt == default || !string.Equals(adapterId, _adapterId, StringComparison.Ordinal))
            {
                return Remember(adapterId, bytesSent, bytesReceived, now, (0, 0));
            }

            var elapsed = now - _measuredAt;
            if (elapsed < MinimumInterval)
            {
                return _last;
            }

            // Counters that went backwards mean they restarted; a rate from that would be nonsense.
            if (bytesSent < _bytesSent || bytesReceived < _bytesReceived)
            {
                return Remember(adapterId, bytesSent, bytesReceived, now, (0, 0));
            }

            var seconds = elapsed.TotalSeconds;
            var speed = ((bytesSent - _bytesSent) / seconds, (bytesReceived - _bytesReceived) / seconds);
            return Remember(adapterId, bytesSent, bytesReceived, now, speed);
        }
    }

    private (double Upload, double Download) Remember(
        string adapterId, long bytesSent, long bytesReceived, DateTime now, (double Upload, double Download) speed)
    {
        _adapterId = adapterId;
        _bytesSent = bytesSent;
        _bytesReceived = bytesReceived;
        _measuredAt = now;
        _last = speed;
        return speed;
    }
}
