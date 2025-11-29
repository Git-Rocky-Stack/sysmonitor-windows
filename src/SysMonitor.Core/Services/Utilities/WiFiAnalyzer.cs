using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

namespace SysMonitor.Core.Services.Utilities;

public class WiFiAnalyzer : IWiFiAnalyzer
{
    public bool IsAvailable => CheckWiFiAvailable();

    public async Task<List<WiFiNetworkInfo>> ScanNetworksAsync(CancellationToken cancellationToken = default)
    {
        var networks = new List<WiFiNetworkInfo>();

        await Task.Run(() =>
        {
            try
            {
                // Use netsh to get WiFi networks - most reliable cross-version approach
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show networks mode=bssid",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process == null) return;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(5000);

                networks.AddRange(ParseNetshOutput(output));
            }
            catch (OperationCanceledException) { throw; }
            catch { }
        }, cancellationToken);

        // Sort by signal strength (strongest first)
        return networks.OrderByDescending(n => n.SignalStrength).ToList();
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
        var lines = output.Split('\n');

        string? currentSSID = null;
        string? currentBSSID = null;
        int signalPercent = 0;
        string networkType = "";
        string authentication = "";
        int channel = 0;
        string band = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !line.Contains("BSSID"))
            {
                // Save previous network if exists
                if (!string.IsNullOrEmpty(currentBSSID))
                {
                    networks.Add(CreateNetworkInfo(currentSSID, currentBSSID, signalPercent,
                        channel, band, authentication, networkType, false));
                }

                var parts = line.Split(':', 2);
                currentSSID = parts.Length > 1 ? parts[1].Trim() : "";
                currentBSSID = null;
            }
            else if (line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase))
            {
                // Save previous BSSID entry if exists (for multiple APs with same SSID)
                if (!string.IsNullOrEmpty(currentBSSID))
                {
                    networks.Add(CreateNetworkInfo(currentSSID, currentBSSID, signalPercent,
                        channel, band, authentication, networkType, false));
                }

                var parts = line.Split(':', 2);
                currentBSSID = parts.Length > 1 ? parts[1].Trim() : "";
            }
            else if (line.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"(\d+)%");
                if (match.Success)
                {
                    signalPercent = int.Parse(match.Groups[1].Value);
                }
            }
            else if (line.StartsWith("Channel", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(':');
                if (parts.Length > 1 && int.TryParse(parts[1].Trim(), out var ch))
                {
                    channel = ch;
                    band = GetBandFromChannel(channel);
                }
            }
            else if (line.StartsWith("Network type", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("Radio type", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(':');
                if (parts.Length > 1)
                {
                    networkType = parts[1].Trim();
                }
            }
            else if (line.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(':');
                if (parts.Length > 1)
                {
                    authentication = parts[1].Trim();
                }
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
            SSID = string.IsNullOrEmpty(ssid) ? "[Hidden Network]" : ssid,
            BSSID = bssid,
            SignalStrength = signalPercent,
            SignalBars = bars,
            SignalQuality = quality,
            SignalColor = color,
            Channel = channel,
            Band = band,
            SecurityType = authentication,
            IsSecured = !authentication.Equals("Open", StringComparison.OrdinalIgnoreCase),
            IsConnected = isConnected,
            FrequencyMHz = GetFrequencyFromChannel(channel),
            NetworkType = networkType
        };
    }

    private static WiFiConnectionInfo? ParseConnectionInfo(string output)
    {
        var lines = output.Split('\n');

        string ssid = "";
        int signal = 0;
        string security = "";
        double speed = 0;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();

            if (line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) && !line.Contains("BSSID"))
            {
                var parts = line.Split(':', 2);
                ssid = parts.Length > 1 ? parts[1].Trim() : "";
            }
            else if (line.StartsWith("Signal", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"(\d+)%");
                if (match.Success)
                {
                    signal = int.Parse(match.Groups[1].Value);
                }
            }
            else if (line.StartsWith("Authentication", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line.Split(':');
                security = parts.Length > 1 ? parts[1].Trim() : "";
            }
            else if (line.StartsWith("Receive rate", StringComparison.OrdinalIgnoreCase) ||
                     line.StartsWith("Transmit rate", StringComparison.OrdinalIgnoreCase))
            {
                var match = Regex.Match(line, @"(\d+\.?\d*)");
                if (match.Success && double.TryParse(match.Groups[1].Value, out var rate))
                {
                    speed = Math.Max(speed, rate);
                }
            }
        }

        if (string.IsNullOrEmpty(ssid))
            return null;

        return new WiFiConnectionInfo
        {
            SSID = ssid,
            SignalStrength = signal,
            IpAddress = GetCurrentIpAddress(),
            LinkSpeedMbps = speed,
            SecurityType = security
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
