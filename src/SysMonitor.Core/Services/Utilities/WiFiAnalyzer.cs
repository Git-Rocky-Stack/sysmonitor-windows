using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace SysMonitor.Core.Services.Utilities;

public class WiFiAnalyzer : IWiFiAnalyzer
{
    private bool? _isAvailableCache;
    private DateTime _lastAvailabilityCheck = DateTime.MinValue;

    public bool IsAvailable
    {
        get
        {
            // Cache the availability check for 5 seconds
            if (_isAvailableCache.HasValue && (DateTime.Now - _lastAvailabilityCheck).TotalSeconds < 5)
                return _isAvailableCache.Value;

            _isAvailableCache = CheckWiFiAvailable();
            _lastAvailabilityCheck = DateTime.Now;
            return _isAvailableCache.Value;
        }
    }

    public async Task<List<WiFiNetworkInfo>> ScanNetworksAsync(CancellationToken cancellationToken = default)
    {
        var networks = new List<WiFiNetworkInfo>();

        await Task.Run(async () =>
        {
            try
            {
                // First, trigger a scan on the wireless interface
                var interfaceName = GetWirelessInterfaceName();
                if (!string.IsNullOrEmpty(interfaceName))
                {
                    try
                    {
                        var scanStartInfo = new ProcessStartInfo
                        {
                            FileName = "netsh",
                            Arguments = $"wlan scan interface=\"{interfaceName}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        };

                        using var scanProcess = Process.Start(scanStartInfo);
                        if (scanProcess != null)
                        {
                            await scanProcess.WaitForExitAsync(cancellationToken);
                            // Wait for the scan to complete
                            await Task.Delay(2500, cancellationToken);
                        }
                    }
                    catch { /* Scan trigger may fail, continue anyway */ }
                }

                // Get the network list
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show networks mode=bssid",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var process = Process.Start(startInfo);
                if (process == null) return;

                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                var parsedNetworks = ParseNetshOutput(output);
                networks.AddRange(parsedNetworks);

                // If netsh parsing failed, try WMI as fallback
                if (networks.Count == 0)
                {
                    var wmiNetworks = await GetNetworksFromWmiAsync(cancellationToken);
                    networks.AddRange(wmiNetworks);
                }

                // If still no networks, try to at least include the connected network
                if (networks.Count == 0)
                {
                    var connected = GetConnectedNetworkAsFallback();
                    if (connected != null)
                    {
                        networks.Add(connected);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }, cancellationToken);

        // Sort by signal strength (strongest first)
        return networks.OrderByDescending(n => n.SignalStrength).ToList();
    }

    private async Task<List<WiFiNetworkInfo>> GetNetworksFromWmiAsync(CancellationToken cancellationToken)
    {
        var networks = new List<WiFiNetworkInfo>();

        try
        {
            await Task.Run(() =>
            {
                // Try to get WiFi networks using WMI (works on Windows 10/11)
                using var searcher = new ManagementObjectSearcher(
                    "root\\WMI",
                    "SELECT * FROM MSNdis_80211_BSSIList");

                // This may not work on all systems, so we wrap in try-catch
            }, cancellationToken);
        }
        catch { }

        return networks;
    }

    private static string? GetWirelessInterfaceName()
    {
        try
        {
            // First try using .NET's NetworkInterface
            var wirelessInterface = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                       nic.OperationalStatus == OperationalStatus.Up);

            if (wirelessInterface != null)
            {
                return wirelessInterface.Name;
            }

            // Fallback to netsh
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            // Parse interface name - look for "Name" or its translations
            var namePattern = new Regex(@"^\s*(Name|Nom|Nombre|名称)\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            var match = namePattern.Match(output);
            if (match.Success)
            {
                return match.Groups[2].Value.Trim();
            }

            // If no match, try any wireless interface
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    return nic.Name;
                }
            }
        }
        catch { }

        return null;
    }

    private WiFiNetworkInfo? GetConnectedNetworkAsFallback()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            var connectionInfo = ParseConnectionInfo(output);
            if (connectionInfo == null || !connectionInfo.IsConnected)
                return null;

            var (quality, color) = GetSignalQuality(connectionInfo.SignalStrength);

            return new WiFiNetworkInfo
            {
                Ssid = connectionInfo.Ssid,
                Bssid = connectionInfo.Bssid,
                SignalStrength = connectionInfo.SignalStrength,
                SignalBars = GetSignalBars(connectionInfo.SignalStrength),
                SignalQuality = quality,
                SignalColor = color,
                Channel = connectionInfo.Channel,
                Band = GetBandFromChannel(connectionInfo.Channel),
                Security = connectionInfo.Security,
                IsSecured = !string.IsNullOrEmpty(connectionInfo.Security) &&
                           !connectionInfo.Security.Equals("Open", StringComparison.OrdinalIgnoreCase),
                IsConnected = true,
                FrequencyMHz = GetFrequencyFromChannel(connectionInfo.Channel),
                NetworkType = "Connected"
            };
        }
        catch { }

        return null;
    }

    public async Task<WiFiAdapterInfo?> GetAdapterInfoAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        return new WiFiAdapterInfo
                        {
                            Name = nic.Name,
                            Description = nic.Description,
                            MacAddress = FormatMacAddress(nic.GetPhysicalAddress().ToString()),
                            IsEnabled = nic.OperationalStatus == OperationalStatus.Up,
                            Status = nic.OperationalStatus.ToString()
                        };
                    }
                }
            }
            catch { }

            return null;
        });
    }

    public async Task<WiFiConnectionInfo?> GetCurrentConnectionAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show interfaces",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return null;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                return ParseConnectionInfo(output);
            }
            catch { }

            return null;
        });
    }

    private static bool CheckWiFiAvailable()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Any(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);
        }
        catch
        {
            return false;
        }
    }

    private static List<WiFiNetworkInfo> ParseNetshOutput(string output)
    {
        var networks = new List<WiFiNetworkInfo>();

        if (string.IsNullOrWhiteSpace(output))
            return networks;

        var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        string? currentSSID = null;
        string? currentBSSID = null;
        int signalPercent = 0;
        string networkType = "";
        string authentication = "";
        int channel = 0;
        string band = "";

        // Common patterns for different locales - use regex for robustness
        var ssidPattern = new Regex(@"^\s*SSID\s*\d*\s*[:：]\s*(.*)$", RegexOptions.IgnoreCase);
        var bssidPattern = new Regex(@"^\s*BSSID\s*\d*\s*[:：]\s*([0-9a-fA-F:]+)", RegexOptions.IgnoreCase);
        var signalPattern = new Regex(@"(\d+)\s*%", RegexOptions.IgnoreCase);
        var channelPattern = new Regex(@"[:：]\s*(\d+)\s*$", RegexOptions.IgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Check for SSID (but not BSSID)
            if (!line.Contains("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                var ssidMatch = ssidPattern.Match(line);
                if (ssidMatch.Success)
                {
                    // Save previous network if exists
                    if (!string.IsNullOrEmpty(currentBSSID))
                    {
                        networks.Add(CreateNetworkInfo(currentSSID, currentBSSID, signalPercent,
                            channel, band, authentication, networkType, false));
                    }

                    currentSSID = ssidMatch.Groups[1].Value.Trim();
                    currentBSSID = null;
                    signalPercent = 0;
                    channel = 0;
                    band = "";
                    authentication = "";
                    networkType = "";
                    continue;
                }
            }

            // Check for BSSID (MAC address pattern)
            var bssidMatch = bssidPattern.Match(line);
            if (bssidMatch.Success || line.Contains("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                // Save previous BSSID entry if exists
                if (!string.IsNullOrEmpty(currentBSSID))
                {
                    networks.Add(CreateNetworkInfo(currentSSID, currentBSSID, signalPercent,
                        channel, band, authentication, networkType, false));
                    signalPercent = 0;
                    channel = 0;
                }

                if (bssidMatch.Success)
                {
                    currentBSSID = bssidMatch.Groups[1].Value.Trim();
                }
                else
                {
                    // Try to extract MAC address from line
                    var macMatch = Regex.Match(line, @"([0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2})");
                    if (macMatch.Success)
                    {
                        currentBSSID = macMatch.Groups[1].Value;
                    }
                }
                continue;
            }

            // Check for Signal strength (look for percentage)
            if (line.Contains("Signal", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("信号", StringComparison.OrdinalIgnoreCase) ||  // Chinese
                line.Contains("Señal", StringComparison.OrdinalIgnoreCase))  // Spanish
            {
                var signalMatch = signalPattern.Match(line);
                if (signalMatch.Success)
                {
                    signalPercent = int.Parse(signalMatch.Groups[1].Value);
                }
                continue;
            }

            // Check for Channel
            if (line.Contains("Channel", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Kanal", StringComparison.OrdinalIgnoreCase) ||   // German
                line.Contains("Canal", StringComparison.OrdinalIgnoreCase) ||   // Spanish/French
                line.Contains("频道", StringComparison.OrdinalIgnoreCase))      // Chinese
            {
                var chMatch = channelPattern.Match(line);
                if (chMatch.Success && int.TryParse(chMatch.Groups[1].Value, out var ch))
                {
                    channel = ch;
                    band = GetBandFromChannel(channel);
                }
                continue;
            }

            // Check for Network/Radio type
            if (line.Contains("Radio type", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Network type", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("802.11", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(new[] { ':', '：' });
                if (parts.Length > 1)
                {
                    networkType = parts[^1].Trim();
                }
                continue;
            }

            // Check for Authentication
            if (line.Contains("Authentication", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Authentifizierung", StringComparison.OrdinalIgnoreCase) ||  // German
                line.Contains("Autenticación", StringComparison.OrdinalIgnoreCase) ||      // Spanish
                line.Contains("认证", StringComparison.OrdinalIgnoreCase))                 // Chinese
            {
                var parts = line.Split(new[] { ':', '：' });
                if (parts.Length > 1)
                {
                    authentication = parts[^1].Trim();
                }
                continue;
            }
        }

        // Don't forget the last network
        if (!string.IsNullOrEmpty(currentBSSID))
        {
            networks.Add(CreateNetworkInfo(currentSSID, currentBSSID, signalPercent,
                channel, band, authentication, networkType, false));
        }

        return networks;
    }

    private static WiFiNetworkInfo CreateNetworkInfo(string? ssid, string bssid, int signalPercent,
        int channel, string band, string authentication, string networkType, bool isConnected)
    {
        var (quality, color) = GetSignalQuality(signalPercent);
        var bars = GetSignalBars(signalPercent);

        return new WiFiNetworkInfo
        {
            Ssid = string.IsNullOrEmpty(ssid) ? "[Hidden Network]" : ssid,
            Bssid = bssid,
            SignalStrength = signalPercent,
            SignalBars = bars,
            SignalQuality = quality,
            SignalColor = color,
            Channel = channel,
            Band = band,
            Security = authentication,
            IsSecured = !authentication.Equals("Open", StringComparison.OrdinalIgnoreCase),
            IsConnected = isConnected,
            FrequencyMHz = GetFrequencyFromChannel(channel),
            NetworkType = networkType
        };
    }

    private static WiFiConnectionInfo? ParseConnectionInfo(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        string ssid = "";
        string bssid = "";
        int signal = 0;
        string security = "";
        int channel = 0;
        int speed = 0;
        string state = "";

        // Check if we have a connected interface
        var statePattern = new Regex(@"State\s*[:：]\s*(.+)", RegexOptions.IgnoreCase);
        var ssidPattern = new Regex(@"^\s*SSID\s*[:：]\s*(.+)$", RegexOptions.IgnoreCase);
        var bssidPattern = new Regex(@"BSSID\s*[:：]\s*([0-9a-fA-F:]+)", RegexOptions.IgnoreCase);
        var signalPattern = new Regex(@"(\d+)\s*%");
        var channelPattern = new Regex(@"[:：]\s*(\d+)\s*$");
        var speedPattern = new Regex(@"(\d+(?:\.\d+)?)\s*(Mbps|Gbps)?", RegexOptions.IgnoreCase);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Check state
            if (line.Contains("State", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("État", StringComparison.OrdinalIgnoreCase) ||   // French
                line.Contains("Estado", StringComparison.OrdinalIgnoreCase) || // Spanish
                line.Contains("状态", StringComparison.OrdinalIgnoreCase))     // Chinese
            {
                var stateMatch = statePattern.Match(line);
                if (stateMatch.Success)
                {
                    state = stateMatch.Groups[1].Value.Trim();
                }
                else
                {
                    var parts = line.Split(new[] { ':', '：' });
                    if (parts.Length > 1) state = parts[^1].Trim();
                }
                continue;
            }

            // Check SSID (but not BSSID)
            if (!line.Contains("BSSID", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("SSID", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("名称", StringComparison.OrdinalIgnoreCase)))
            {
                var ssidMatch = ssidPattern.Match(line);
                if (ssidMatch.Success)
                {
                    ssid = ssidMatch.Groups[1].Value.Trim();
                }
                else
                {
                    var parts = line.Split(new[] { ':', '：' }, 2);
                    if (parts.Length > 1) ssid = parts[1].Trim();
                }
                continue;
            }

            // Check BSSID
            if (line.Contains("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                var bssidMatch = bssidPattern.Match(line);
                if (bssidMatch.Success)
                {
                    bssid = bssidMatch.Groups[1].Value.Trim();
                }
                else
                {
                    // Try to find MAC address pattern
                    var macMatch = Regex.Match(line, @"([0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2})");
                    if (macMatch.Success)
                    {
                        bssid = macMatch.Groups[1].Value;
                    }
                }
                continue;
            }

            // Check Signal
            if (line.Contains("Signal", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("信号", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Señal", StringComparison.OrdinalIgnoreCase))
            {
                var signalMatch = signalPattern.Match(line);
                if (signalMatch.Success)
                {
                    signal = int.Parse(signalMatch.Groups[1].Value);
                }
                continue;
            }

            // Check Channel
            if (line.Contains("Channel", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Kanal", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Canal", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("频道", StringComparison.OrdinalIgnoreCase))
            {
                var chMatch = channelPattern.Match(line);
                if (chMatch.Success && int.TryParse(chMatch.Groups[1].Value, out var ch))
                {
                    channel = ch;
                }
                continue;
            }

            // Check Authentication/Security
            if (line.Contains("Authentication", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Authentifizierung", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Autenticación", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("认证", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(new[] { ':', '：' });
                if (parts.Length > 1) security = parts[^1].Trim();
                continue;
            }

            // Check Speed (Receive/Transmit rate)
            if (line.Contains("rate", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("Geschwindigkeit", StringComparison.OrdinalIgnoreCase) ||  // German
                line.Contains("velocidad", StringComparison.OrdinalIgnoreCase) ||        // Spanish
                line.Contains("速率", StringComparison.OrdinalIgnoreCase))               // Chinese
            {
                var speedMatch = speedPattern.Match(line);
                if (speedMatch.Success && double.TryParse(speedMatch.Groups[1].Value, out var rate))
                {
                    var rateInt = (int)rate;
                    if (speedMatch.Groups[2].Value.Equals("Gbps", StringComparison.OrdinalIgnoreCase))
                        rateInt = (int)(rate * 1000);
                    speed = Math.Max(speed, rateInt);
                }
                continue;
            }
        }

        // Check if actually connected
        if (string.IsNullOrEmpty(ssid))
            return null;

        // If state indicates disconnected, return null
        if (!string.IsNullOrEmpty(state) &&
            (state.Contains("disconnected", StringComparison.OrdinalIgnoreCase) ||
             state.Contains("getrennt", StringComparison.OrdinalIgnoreCase) ||      // German
             state.Contains("desconectado", StringComparison.OrdinalIgnoreCase)))   // Spanish
        {
            return null;
        }

        return new WiFiConnectionInfo
        {
            Ssid = ssid,
            Bssid = bssid,
            SignalStrength = signal,
            Channel = channel,
            Security = security,
            LinkSpeed = speed,
            IpAddress = GetCurrentIpAddress(),
            IsConnected = true
        };
    }

    private static string GetCurrentIpAddress()
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                    nic.OperationalStatus == OperationalStatus.Up)
                {
                    var props = nic.GetIPProperties();
                    foreach (var addr in props.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            return addr.Address.ToString();
                        }
                    }
                }
            }
        }
        catch { }

        return "";
    }

    private static (string quality, string color) GetSignalQuality(int percent)
    {
        return percent switch
        {
            >= 80 => ("Excellent", "#4CAF50"),
            >= 60 => ("Good", "#8BC34A"),
            >= 40 => ("Fair", "#FF9800"),
            >= 20 => ("Weak", "#FF5722"),
            _ => ("Very Weak", "#F44336")
        };
    }

    private static int GetSignalBars(int percent)
    {
        return percent switch
        {
            >= 80 => 5,
            >= 60 => 4,
            >= 40 => 3,
            >= 20 => 2,
            _ => 1
        };
    }

    private static string GetBandFromChannel(int channel)
    {
        return channel <= 14 ? "2.4 GHz" : "5 GHz";
    }

    private static double GetFrequencyFromChannel(int channel)
    {
        if (channel <= 14)
        {
            // 2.4 GHz band
            return 2412 + (channel - 1) * 5;
        }
        else
        {
            // 5 GHz band (simplified)
            return channel switch
            {
                36 => 5180,
                40 => 5200,
                44 => 5220,
                48 => 5240,
                52 => 5260,
                56 => 5280,
                60 => 5300,
                64 => 5320,
                100 => 5500,
                104 => 5520,
                108 => 5540,
                112 => 5560,
                116 => 5580,
                120 => 5600,
                124 => 5620,
                128 => 5640,
                132 => 5660,
                136 => 5680,
                140 => 5700,
                144 => 5720,
                149 => 5745,
                153 => 5765,
                157 => 5785,
                161 => 5805,
                165 => 5825,
                _ => 5000 + channel * 5
            };
        }
    }

    private static string FormatMacAddress(string mac)
    {
        if (mac.Length != 12) return mac;
        return string.Join(":", Enumerable.Range(0, 6).Select(i => mac.Substring(i * 2, 2)));
    }
}
