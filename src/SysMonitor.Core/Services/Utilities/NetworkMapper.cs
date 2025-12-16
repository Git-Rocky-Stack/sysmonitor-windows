using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace SysMonitor.Core.Services.Utilities;

public class NetworkMapper : INetworkMapper
{
    private readonly ILogger<NetworkMapper> _logger;

    private static readonly Dictionary<int, string> CommonPorts = new()
    {
        { 21, "FTP" },
        { 22, "SSH" },
        { 23, "Telnet" },
        { 25, "SMTP" },
        { 53, "DNS" },
        { 80, "HTTP" },
        { 110, "POP3" },
        { 135, "RPC" },
        { 139, "NetBIOS" },
        { 143, "IMAP" },
        { 443, "HTTPS" },
        { 445, "SMB" },
        { 3306, "MySQL" },
        { 3389, "RDP" },
        { 5432, "PostgreSQL" },
        { 5900, "VNC" },
        { 8080, "HTTP Alt" },
        { 8443, "HTTPS Alt" }
    };

    public NetworkMapper(ILogger<NetworkMapper> logger)
    {
        _logger = logger;
    }

    public async Task<List<NetworkDeviceInfo>> ScanNetworkAsync(string subnet,
        IProgress<NetworkScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var devices = new List<NetworkDeviceInfo>();

        var ips = GenerateIpRange(subnet);
        var total = ips.Count;
        var scanned = 0;

        // Parallel ping with limited concurrency (reduced from 50 to avoid memory pressure)
        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = 10,
            CancellationToken = cancellationToken
        };

        var lockObj = new object();

        await Parallel.ForEachAsync(ips, options, async (ip, ct) =>
        {
            var device = await PingAndDiscoverAsync(ip, ct);

            lock (lockObj)
            {
                scanned++;
                if (device != null)
                {
                    devices.Add(device);
                }

                if (scanned % 10 == 0 || scanned == total)
                {
                    progress?.Report(new NetworkScanProgress
                    {
                        ScannedCount = scanned,
                        TotalCount = total,
                        CurrentTarget = ip,
                        Status = $"Scanning {ip}...",
                        FoundDevice = device
                    });
                }
            }
        });

        return devices.OrderBy(d => IPAddress.Parse(d.IpAddress).GetAddressBytes()[3]).ToList();
    }

    public async Task<NetworkDeviceInfo?> GetDeviceInfoAsync(string ipAddress)
    {
        return await PingAndDiscoverAsync(ipAddress, CancellationToken.None);
    }

    public async Task<LocalNetworkInfo> GetLocalNetworkInfoAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus == OperationalStatus.Up &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    {
                        var props = nic.GetIPProperties();

                        foreach (var addr in props.UnicastAddresses)
                        {
                            if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                            {
                                var ip = addr.Address.ToString();
                                var mask = addr.IPv4Mask?.ToString() ?? "255.255.255.0";
                                var gateway = props.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? "";
                                var dns = props.DnsAddresses.FirstOrDefault()?.ToString() ?? "";
                                var mac = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));

                                return new LocalNetworkInfo
                                {
                                    LocalIpAddress = ip,
                                    SubnetMask = mask,
                                    Gateway = gateway,
                                    MacAddress = mac,
                                    Hostname = Environment.MachineName,
                                    NetworkName = nic.Name,
                                    DnsServer = dns,
                                    AdapterName = nic.Name
                                };
                            }
                        }
                    }
                }
            }
            catch { }

            return new LocalNetworkInfo();
        });
    }

    public async Task<List<PortInfo>> ScanPortsAsync(string ipAddress, int[] ports, CancellationToken cancellationToken = default)
    {
        var results = new List<PortInfo>();

        var tasks = ports.Select(async port =>
        {
            var isOpen = await IsPortOpenAsync(ipAddress, port, 500, cancellationToken);
            return new PortInfo
            {
                Port = port,
                ServiceName = CommonPorts.TryGetValue(port, out var name) ? name : "Unknown",
                IsOpen = isOpen,
                Protocol = "TCP"
            };
        });

        var portResults = await Task.WhenAll(tasks);
        return portResults.Where(p => p.IsOpen).ToList();
    }

    private async Task<NetworkDeviceInfo?> PingAndDiscoverAsync(string ipAddress, CancellationToken cancellationToken)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(ipAddress, 1000);

            if (reply.Status != IPStatus.Success)
                return null;

            var mac = GetMacAddress(ipAddress);
            var hostname = await GetHostnameAsync(ipAddress);
            var manufacturer = GetManufacturer(mac);
            var deviceType = DetermineDeviceType(hostname, manufacturer, mac);
            var (status, color) = GetResponseStatus(reply.RoundtripTime);

            return new NetworkDeviceInfo
            {
                IpAddress = ipAddress,
                MacAddress = mac,
                Hostname = hostname,
                Manufacturer = manufacturer,
                DeviceType = deviceType.type,
                DeviceIcon = deviceType.icon,
                IsOnline = true,
                ResponseTimeMs = (int)reply.RoundtripTime,
                ResponseStatus = status,
                ResponseColor = color,
                LastSeen = DateTime.Now
            };
        }
        catch
        {
            return null;
        }
    }

    private static List<string> GenerateIpRange(string subnet)
    {
        var ips = new List<string>();

        // Parse subnet like "192.168.1.0/24" or just "192.168.1"
        var parts = subnet.Replace("/24", "").Split('.');
        if (parts.Length < 3) return ips;

        var prefix = $"{parts[0]}.{parts[1]}.{parts[2]}";

        for (int i = 1; i <= 254; i++)
        {
            ips.Add($"{prefix}.{i}");
        }

        return ips;
    }

    private static string CalculateNetworkRange(string ip, string mask)
    {
        var ipParts = ip.Split('.').Select(int.Parse).ToArray();
        var maskParts = mask.Split('.').Select(int.Parse).ToArray();

        var network = new int[4];
        for (int i = 0; i < 4; i++)
        {
            network[i] = ipParts[i] & maskParts[i];
        }

        return $"{network[0]}.{network[1]}.{network[2]}.1 - {network[0]}.{network[1]}.{network[2]}.254";
    }

    [DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern int SendARP(int destIp, int srcIp, byte[] macAddr, ref int physicalAddrLen);

    private static string GetMacAddress(string ipAddress)
    {
        try
        {
            var dest = BitConverter.ToInt32(IPAddress.Parse(ipAddress).GetAddressBytes(), 0);
            var macAddr = new byte[6];
            var macAddrLen = macAddr.Length;

            if (SendARP(dest, 0, macAddr, ref macAddrLen) == 0)
            {
                return string.Join(":", macAddr.Take(macAddrLen).Select(b => b.ToString("X2")));
            }
        }
        catch { }

        return "";
    }

    private static async Task<string> GetHostnameAsync(string ipAddress)
    {
        try
        {
            var hostEntry = await Dns.GetHostEntryAsync(ipAddress);
            return hostEntry.HostName;
        }
        catch
        {
            return "";
        }
    }

    private static string GetManufacturer(string macAddress)
    {
        if (string.IsNullOrEmpty(macAddress))
            return "";

        // Extract OUI (first 3 bytes)
        var oui = macAddress.Replace(":", "").Substring(0, 6).ToUpperInvariant();

        // Common manufacturer OUIs (abbreviated list)
        var manufacturers = new Dictionary<string, string>
        {
            { "001A2B", "Apple" },
            { "00163E", "Xensource" },
            { "000C29", "VMware" },
            { "005056", "VMware" },
            { "0050F2", "Microsoft" },
            { "001DD8", "Microsoft" },
            { "001E68", "Quanta" },
            { "002590", "Super Micro" },
            { "00155D", "Microsoft Hyper-V" },
            { "00505A", "Cisco" },
            { "000142", "Cisco" },
            { "001B78", "Hewlett-Packard" },
            { "001CC4", "Hewlett-Packard" },
            { "00219B", "Dell" },
            { "001422", "Dell" },
            { "B8AC6F", "Dell" },
            { "00E04C", "Realtek" },
            { "001731", "Realtek" },
            { "40F02F", "LiteOn" },
            { "00265E", "AzureWave" },
            { "E8039A", "Samsung" },
            { "98052E", "Samsung" },
            { "D4619D", "Samsung" },
            { "8C8590", "Apple" },
            { "A4C361", "Apple" },
            { "14109F", "Apple" },
            { "ACC334", "TP-LINK" },
            { "50C7BF", "TP-LINK" },
            { "C46E1F", "TP-LINK" },
            { "005079", "Private" },
            { "00005E", "IANA" },
            { "00B0D0", "Dell" },
            { "74D435", "Giga-Byte" },
            { "1C6F65", "Giga-Byte" },
            { "D4BE2F", "Intel" },
            { "3C22FB", "Intel" },
            { "B4B52F", "Hewlett-Packard" }
        };

        return manufacturers.TryGetValue(oui, out var name) ? name : "";
    }

    private static (string type, string icon) DetermineDeviceType(string hostname, string manufacturer, string mac)
    {
        var hostLower = hostname.ToLowerInvariant();
        var mfgLower = manufacturer.ToLowerInvariant();

        // Check hostname patterns
        if (hostLower.Contains("iphone") || hostLower.Contains("android") || hostLower.Contains("galaxy"))
            return ("Phone", "\uE8EA");

        if (hostLower.Contains("ipad") || hostLower.Contains("tablet"))
            return ("Tablet", "\uE70A");

        if (hostLower.Contains("macbook") || hostLower.Contains("laptop"))
            return ("Laptop", "\uE7F8");

        if (hostLower.Contains("printer") || hostLower.Contains("print"))
            return ("Printer", "\uE749");

        if (hostLower.Contains("router") || hostLower.Contains("gateway"))
            return ("Router", "\uE839");

        if (hostLower.Contains("nas") || hostLower.Contains("storage"))
            return ("NAS", "\uE8B7");

        if (hostLower.Contains("camera") || hostLower.Contains("cam"))
            return ("Camera", "\uE8B8");

        if (hostLower.Contains("tv") || hostLower.Contains("roku") || hostLower.Contains("chromecast"))
            return ("Smart TV", "\uE7F4");

        // Check manufacturer
        if (mfgLower.Contains("apple"))
            return ("Apple Device", "\uE8EA");

        if (mfgLower.Contains("samsung"))
            return ("Samsung Device", "\uE8EA");

        if (mfgLower.Contains("vmware") || mfgLower.Contains("hyper-v"))
            return ("Virtual Machine", "\uE770");

        if (mfgLower.Contains("cisco"))
            return ("Network Device", "\uE839");

        if (mfgLower.Contains("tp-link"))
            return ("Network Device", "\uE839");

        return ("Computer", "\uE7F8");
    }

    private static (string status, string color) GetResponseStatus(long ms)
    {
        return ms switch
        {
            < 10 => ("Excellent", "#4CAF50"),
            < 50 => ("Good", "#8BC34A"),
            < 100 => ("Fair", "#FF9800"),
            < 200 => ("Slow", "#FF5722"),
            _ => ("Very Slow", "#F44336")
        };
    }

    private static async Task<bool> IsPortOpenAsync(string host, int port, int timeout, CancellationToken ct)
    {
        try
        {
            using var client = new TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            await client.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
