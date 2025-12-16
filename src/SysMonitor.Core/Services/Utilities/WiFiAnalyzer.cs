using System.Diagnostics;
using System.Management;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;

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

        // ALWAYS use netsh first - it's most reliable for channel/frequency data on unpackaged apps
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
                            await Task.Delay(2000, cancellationToken);
                        }
                    }
                    catch { /* Scan trigger may fail, continue anyway */ }
                }

                // Try direct netsh with cmd.exe UTF-8 first - most reliable
                var cmdStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c chcp 65001 >nul && netsh wlan show networks mode=bssid",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var cmdProcess = Process.Start(cmdStartInfo);
                if (cmdProcess != null)
                {
                    var output = await cmdProcess.StandardOutput.ReadToEndAsync(cancellationToken);
                    await cmdProcess.WaitForExitAsync(cancellationToken);

                    System.Diagnostics.Debug.WriteLine($"[WiFi] netsh output length: {output?.Length ?? 0}");
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        System.Diagnostics.Debug.WriteLine($"[WiFi] netsh output preview: {output.Substring(0, Math.Min(500, output.Length))}");
                    }

                    var parsedNetworks = ParseNetshOutput(output ?? "");
                    System.Diagnostics.Debug.WriteLine($"[WiFi] Parsed {parsedNetworks.Count} networks from netsh");

                    foreach (var net in parsedNetworks)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WiFi] Network: {net.Ssid}, Ch={net.Channel}, Freq={net.FrequencyMHz}, Band={net.Band}");
                    }

                    networks.AddRange(parsedNetworks);
                }

                // If netsh didn't work, try PowerShell approach
                if (networks.Count == 0)
                {
                    var psNetworks = await GetNetworksFromPowerShellAsync(cancellationToken);
                    if (psNetworks.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WiFi] PowerShell found {psNetworks.Count} networks");
                        networks.AddRange(psNetworks);
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WiFi] netsh scan error: {ex.Message}");
            }
        }, cancellationToken);

        // Try Windows Runtime API as secondary source (may have better signal strength data)
        if (networks.Count == 0)
        {
            try
            {
                var winRtNetworks = await GetNetworksFromWinRTAsync(cancellationToken);
                if (winRtNetworks.Count > 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[WiFi] WinRT found {winRtNetworks.Count} networks");
                    networks.AddRange(winRtNetworks);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WiFi] WinRT scan failed: {ex.Message}");
            }
        }

        // Always try to get accurate connected network info and merge/update
        try
        {
            var connectionInfo = GetConnectionInfoFromCmd() ?? GetConnectionInfoFromPowerShell();
            if (connectionInfo != null && connectionInfo.IsConnected && connectionInfo.Channel > 0)
            {
                var (quality, color) = GetSignalQuality(connectionInfo.SignalStrength);
                var band = connectionInfo.Band;
                if (string.IsNullOrEmpty(band) || band == "Unknown")
                {
                    band = GetBandFromChannel(connectionInfo.Channel);
                }

                var connected = new WiFiNetworkInfo
                {
                    Ssid = connectionInfo.Ssid,
                    Bssid = connectionInfo.Bssid,
                    SignalStrength = connectionInfo.SignalStrength,
                    SignalBars = GetSignalBars(connectionInfo.SignalStrength),
                    SignalQuality = quality,
                    SignalColor = color,
                    Channel = connectionInfo.Channel,
                    Band = band,
                    Security = connectionInfo.Security,
                    IsSecured = !string.IsNullOrEmpty(connectionInfo.Security) &&
                               !connectionInfo.Security.Equals("Open", StringComparison.OrdinalIgnoreCase),
                    IsConnected = true,
                    FrequencyMHz = GetFrequencyFromChannel(connectionInfo.Channel),
                    NetworkType = connectionInfo.RadioType
                };

                // Check if this network is already in the list
                var existingNetwork = networks.FirstOrDefault(n =>
                    n.Ssid.Equals(connected.Ssid, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(n.Bssid) && n.Bssid.Equals(connected.Bssid, StringComparison.OrdinalIgnoreCase)));

                if (existingNetwork == null)
                {
                    networks.Insert(0, connected);
                }
                else
                {
                    // Update the existing entry with accurate connection info
                    var index = networks.IndexOf(existingNetwork);
                    networks[index] = connected;
                }
            }
            else
            {
                // Fallback to old method
                var connectedFallback = GetConnectedNetworkAsFallback();
                if (connectedFallback != null)
                {
                    // Mark as connected
                    var existingNetwork = networks.FirstOrDefault(n =>
                        n.Ssid.Equals(connectedFallback.Ssid, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(n.Bssid) && n.Bssid.Equals(connectedFallback.Bssid, StringComparison.OrdinalIgnoreCase)));

                    if (existingNetwork == null)
                    {
                        networks.Insert(0, connectedFallback);
                    }
                    else
                    {
                        var index = networks.IndexOf(existingNetwork);
                        networks[index] = connectedFallback with { IsConnected = true };
                    }
                }
            }
        }
        catch { }

        // Sort by connected first, then by signal strength
        return networks
            .OrderByDescending(n => n.IsConnected)
            .ThenByDescending(n => n.SignalStrength)
            .ToList();
    }

    private static async Task<List<WiFiNetworkInfo>> GetNetworksFromPowerShellAsync(CancellationToken cancellationToken)
    {
        var networks = new List<WiFiNetworkInfo>();

        try
        {
            // Use PowerShell to run netsh - simpler command
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"netsh wlan show networks mode=bssid\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);

                if (!string.IsNullOrWhiteSpace(output))
                {
                    networks = ParseNetshOutput(output);
                }
            }
        }
        catch { }

        return networks;
    }

    private async Task<List<WiFiNetworkInfo>> GetNetworksFromWinRTAsync(CancellationToken cancellationToken)
    {
        var networks = new List<WiFiNetworkInfo>();

        try
        {
            // Request access to WiFi adapter
            var access = await WiFiAdapter.RequestAccessAsync();
            if (access != WiFiAccessStatus.Allowed)
                return networks;

            // Find available WiFi adapters
            var adapters = await WiFiAdapter.FindAllAdaptersAsync();
            if (adapters.Count == 0)
                return networks;

            var adapter = adapters[0];

            // Scan for networks
            await adapter.ScanAsync();

            // Get the network report
            var report = adapter.NetworkReport;

            foreach (var network in report.AvailableNetworks)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ssid = network.Ssid;
                var bssid = network.Bssid;
                var signalBars = (int)network.SignalBars;
                var signalPercent = SignalBarsToPercent(signalBars);
                var channel = (int)network.ChannelCenterFrequencyInKilohertz / 1000; // Convert kHz to MHz for freq
                var freqMHz = (int)(network.ChannelCenterFrequencyInKilohertz / 1000);
                var actualChannel = GetChannelFromFrequency(freqMHz);
                var band = GetBandFromFrequency(freqMHz);
                var security = GetSecurityString(network.SecuritySettings.NetworkAuthenticationType);
                var networkType = network.PhyKind.ToString();

                var (quality, color) = GetSignalQuality(signalPercent);

                networks.Add(new WiFiNetworkInfo
                {
                    Ssid = string.IsNullOrEmpty(ssid) ? "[Hidden Network]" : ssid,
                    Bssid = bssid,
                    SignalStrength = signalPercent,
                    SignalBars = signalBars,
                    SignalQuality = quality,
                    SignalColor = color,
                    Channel = actualChannel,
                    Band = band,
                    Security = security,
                    IsSecured = network.SecuritySettings.NetworkAuthenticationType != NetworkAuthenticationType.Open80211,
                    IsConnected = false,
                    FrequencyMHz = freqMHz,
                    NetworkType = networkType
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"WinRT WiFi scan failed: {ex.Message}");
        }

        return networks;
    }

    private static int SignalBarsToPercent(int bars)
    {
        return bars switch
        {
            5 => 100,
            4 => 80,
            3 => 60,
            2 => 40,
            1 => 20,
            _ => 0
        };
    }

    private static int GetChannelFromFrequency(int freqMHz)
    {
        // 2.4 GHz band (2412-2484 MHz)
        if (freqMHz >= 2412 && freqMHz <= 2484)
        {
            if (freqMHz == 2484) return 14; // Japan only
            return (freqMHz - 2412) / 5 + 1;
        }

        // 5 GHz band
        if (freqMHz >= 5170 && freqMHz <= 5825)
        {
            return (freqMHz - 5000) / 5;
        }

        // 6 GHz band (WiFi 6E)
        if (freqMHz >= 5925 && freqMHz <= 7125)
        {
            return (freqMHz - 5950) / 5 + 1;
        }

        return 0;
    }

    private static string GetBandFromFrequency(int freqMHz)
    {
        if (freqMHz >= 2400 && freqMHz < 2500)
            return "2.4 GHz";
        if (freqMHz >= 5000 && freqMHz < 5900)
            return "5 GHz";
        if (freqMHz >= 5925 && freqMHz <= 7125)
            return "6 GHz";
        return "Unknown";
    }

    private static string GetSecurityString(NetworkAuthenticationType authType)
    {
        return authType switch
        {
            NetworkAuthenticationType.None => "None",
            NetworkAuthenticationType.Unknown => "Unknown",
            NetworkAuthenticationType.Open80211 => "Open",
            NetworkAuthenticationType.SharedKey80211 => "WEP",
            NetworkAuthenticationType.Wpa => "WPA",
            NetworkAuthenticationType.WpaPsk => "WPA-PSK",
            NetworkAuthenticationType.WpaNone => "WPA-None",
            NetworkAuthenticationType.Rsna => "WPA2",
            NetworkAuthenticationType.RsnaPsk => "WPA2-Personal",
            NetworkAuthenticationType.Ihv => "IHV",
            NetworkAuthenticationType.Wpa3Sae => "WPA3-Personal",
            NetworkAuthenticationType.Owe => "OWE",
            _ => authType.ToString()
        };
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
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(850) // OEM code page
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
        // First try netsh
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(850) // OEM code page for Windows console
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);

                var connectionInfo = ParseConnectionInfo(output);
                if (connectionInfo != null && connectionInfo.IsConnected)
                {
                    var (quality, color) = GetSignalQuality(connectionInfo.SignalStrength);

                    // Use parsed band if available, otherwise determine from channel
                    var band = connectionInfo.Band;
                    if (string.IsNullOrEmpty(band) || band == "Unknown")
                    {
                        band = GetBandFromChannel(connectionInfo.Channel);
                    }

                    return new WiFiNetworkInfo
                    {
                        Ssid = connectionInfo.Ssid,
                        Bssid = connectionInfo.Bssid,
                        SignalStrength = connectionInfo.SignalStrength,
                        SignalBars = GetSignalBars(connectionInfo.SignalStrength),
                        SignalQuality = quality,
                        SignalColor = color,
                        Channel = connectionInfo.Channel,
                        Band = band,
                        Security = connectionInfo.Security,
                        IsSecured = !string.IsNullOrEmpty(connectionInfo.Security) &&
                                   !connectionInfo.Security.Equals("Open", StringComparison.OrdinalIgnoreCase),
                        IsConnected = true,
                        FrequencyMHz = GetFrequencyFromChannel(connectionInfo.Channel),
                        NetworkType = connectionInfo.RadioType
                    };
                }
            }
        }
        catch { }

        // Fallback: Use NetworkInterface to build basic info
        try
        {
            var wirelessAdapter = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                       nic.OperationalStatus == OperationalStatus.Up);

            if (wirelessAdapter != null)
            {
                var ssid = GetConnectedSsidFromProfile() ?? wirelessAdapter.Name;
                var speed = (int)(wirelessAdapter.Speed / 1_000_000);
                var (quality, color) = GetSignalQuality(75); // Estimated

                return new WiFiNetworkInfo
                {
                    Ssid = ssid,
                    Bssid = FormatMacAddress(wirelessAdapter.GetPhysicalAddress().ToString()),
                    SignalStrength = 75, // Estimated
                    SignalBars = 4,
                    SignalQuality = quality,
                    SignalColor = color,
                    Channel = 0,
                    Band = "Unknown",
                    Security = "WPA2",
                    IsSecured = true,
                    IsConnected = true,
                    FrequencyMHz = 0,
                    NetworkType = $"{speed} Mbps"
                };
            }
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
                // Get all wireless adapters and prefer the one that's connected
                var wirelessAdapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    .ToList();

                // Prefer connected adapter
                var adapter = wirelessAdapters.FirstOrDefault(nic => nic.OperationalStatus == OperationalStatus.Up)
                              ?? wirelessAdapters.FirstOrDefault();

                if (adapter != null)
                {
                    return new WiFiAdapterInfo
                    {
                        Name = adapter.Name,
                        Description = adapter.Description,
                        MacAddress = FormatMacAddress(adapter.GetPhysicalAddress().ToString()),
                        IsEnabled = adapter.OperationalStatus == OperationalStatus.Up,
                        Status = adapter.OperationalStatus == OperationalStatus.Up ? "Connected" : adapter.OperationalStatus.ToString()
                    };
                }

                // Fallback: Try to get adapter info from WMI
                using var searcher = new ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID LIKE '%Wi-Fi%' OR Name LIKE '%Wireless%' OR Name LIKE '%WiFi%' OR Name LIKE '%WLAN%'");

                foreach (ManagementObject obj in searcher.Get())
                {
                    var name = obj["Name"]?.ToString() ?? "WiFi Adapter";
                    var mac = obj["MACAddress"]?.ToString() ?? "";
                    var status = obj["NetConnectionStatus"]?.ToString();
                    var isEnabled = status == "2"; // 2 = Connected

                    return new WiFiAdapterInfo
                    {
                        Name = obj["NetConnectionID"]?.ToString() ?? name,
                        Description = name,
                        MacAddress = mac,
                        IsEnabled = isEnabled,
                        Status = isEnabled ? "Connected" : "Disconnected"
                    };
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
                // Try cmd.exe with UTF-8 code page first - most reliable
                var cmdInfo = GetConnectionInfoFromCmd();
                if (cmdInfo != null && cmdInfo.Channel > 0)
                {
                    return cmdInfo;
                }

                // Try PowerShell as second option
                var psInfo = GetConnectionInfoFromPowerShell();
                if (psInfo != null && psInfo.Channel > 0)
                {
                    return psInfo;
                }

                // Fallback to netsh with default encoding
                var startInfo = new ProcessStartInfo
                {
                    FileName = "netsh",
                    Arguments = "wlan show interfaces",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);

                    var connectionInfo = ParseConnectionInfo(output);
                    if (connectionInfo != null)
                    {
                        // If we got PS/cmd info but no channel, merge the data
                        var bestInfo = cmdInfo ?? psInfo;
                        if (bestInfo != null && connectionInfo.Channel == 0 && bestInfo.Channel > 0)
                        {
                            return connectionInfo with
                            {
                                Channel = bestInfo.Channel,
                                Band = bestInfo.Band,
                                RadioType = bestInfo.RadioType
                            };
                        }
                        return connectionInfo;
                    }
                }
            }
            catch { }

            // Fallback: Build connection info from NetworkInterface
            try
            {
                var wirelessAdapter = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                                           nic.OperationalStatus == OperationalStatus.Up);

                if (wirelessAdapter != null)
                {
                    var ipProps = wirelessAdapter.GetIPProperties();
                    var ipAddress = ipProps.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?
                        .Address.ToString() ?? "";

                    var gateway = ipProps.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? "";

                    // Try to get SSID from profile
                    var ssid = GetConnectedSsidFromProfile() ?? wirelessAdapter.Name;

                    // Get speed
                    var speed = (int)(wirelessAdapter.Speed / 1_000_000); // Convert to Mbps

                    return new WiFiConnectionInfo
                    {
                        Ssid = ssid,
                        Bssid = FormatMacAddress(wirelessAdapter.GetPhysicalAddress().ToString()),
                        SignalStrength = 75, // Estimated since we can't get exact signal from NetworkInterface
                        Channel = 0, // Not available from NetworkInterface
                        Security = "WPA2", // Assumed
                        LinkSpeed = speed,
                        IpAddress = ipAddress,
                        IsConnected = true
                    };
                }
            }
            catch { }

            return null;
        });
    }

    private static WiFiConnectionInfo? GetConnectionInfoFromCmd()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine("[WiFi Cmd] Starting netsh wlan show interfaces via cmd...");

            // Use cmd.exe with chcp 65001 for UTF-8 output
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c chcp 65001 >nul && netsh wlan show interfaces",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                System.Diagnostics.Debug.WriteLine("[WiFi Cmd] Failed to start process");
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            System.Diagnostics.Debug.WriteLine($"[WiFi Cmd] Output length: {output?.Length ?? 0}");
            if (!string.IsNullOrEmpty(error))
            {
                System.Diagnostics.Debug.WriteLine($"[WiFi Cmd] Error: {error}");
            }

            if (!string.IsNullOrWhiteSpace(output))
            {
                // Log first 1000 chars of raw output for debugging
                var preview = output.Length > 1000 ? output.Substring(0, 1000) : output;
                System.Diagnostics.Debug.WriteLine($"[WiFi Cmd] Raw output:\n{preview}");
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                System.Diagnostics.Debug.WriteLine("[WiFi Cmd] Empty output received");
                return null;
            }

            return ParseWiFiInterfaceOutput(output);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WiFi Cmd] Exception: {ex.Message}");
        }

        return null;
    }

    private static WiFiConnectionInfo? GetConnectionInfoFromPowerShell()
    {
        try
        {
            // Use PowerShell to run netsh - simpler command that works reliably
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command \"netsh wlan show interfaces\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            if (string.IsNullOrWhiteSpace(output))
                return null;

            return ParseWiFiInterfaceOutput(output);
        }
        catch { }

        return null;
    }

    private static WiFiConnectionInfo? ParseWiFiInterfaceOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            System.Diagnostics.Debug.WriteLine("[WiFi Interface] Empty output");
            return null;
        }

        System.Diagnostics.Debug.WriteLine($"[WiFi Interface] Parsing output ({output.Length} chars)");

        string ssid = "";
        string bssid = "";
        int signal = 0;
        string security = "";
        int channel = 0;
        int speed = 0;
        string state = "";
        string band = "";
        string radioType = "";

        var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Find the colon separator (handle multiple colons by taking first)
            var colonIndex = line.IndexOf(':');
            if (colonIndex <= 0) continue;

            var key = line.Substring(0, colonIndex).Trim().ToLowerInvariant();
            var value = line.Substring(colonIndex + 1).Trim();

            if (string.IsNullOrEmpty(value)) continue;

            // Debug key fields
            if (key.Contains("channel") || key.Contains("ssid") || key.Contains("signal") || key.Contains("state") || key.Contains("radio") || key.Contains("band"))
            {
                System.Diagnostics.Debug.WriteLine($"[WiFi Interface] Key='{key}' Value='{value}'");
            }

            // State - check various forms
            if (key == "state" || key.EndsWith("state") || key.Contains("state") || key.Contains("status") || key.Contains("estado") || key.Contains("état") || key.Contains("zustand"))
            {
                state = value;
                continue;
            }

            // SSID (not BSSID) - check the key doesn't contain 'bssid'
            if ((key == "ssid" || key.EndsWith("ssid") || key.StartsWith("ssid")) && !key.Contains("bssid"))
            {
                ssid = value;
                System.Diagnostics.Debug.WriteLine($"[WiFi Interface] Found SSID: {ssid}");
                continue;
            }

            // BSSID
            if (key == "bssid" || key.EndsWith("bssid") || key.Contains("bssid"))
            {
                bssid = value;
                continue;
            }

            // Signal
            if (key == "signal" || key.EndsWith("signal") || key.Contains("signal") || key.Contains("señal") || key.Contains("信号"))
            {
                var signalMatch = Regex.Match(value, @"(\d+)");
                if (signalMatch.Success && int.TryParse(signalMatch.Groups[1].Value, out var sig))
                {
                    signal = sig;
                    System.Diagnostics.Debug.WriteLine($"[WiFi Interface] Signal: {signal}%");
                }
                continue;
            }

            // Channel - be more permissive
            if (key == "channel" || key.EndsWith("channel") || key.Contains("channel") || key.Contains("kanal") || key.Contains("canal") || key.Contains("频道"))
            {
                if (int.TryParse(value, out var ch))
                {
                    channel = ch;
                    System.Diagnostics.Debug.WriteLine($"[WiFi Interface] Channel: {channel}");
                }
                else
                {
                    var chMatch = Regex.Match(value, @"(\d+)");
                    if (chMatch.Success && int.TryParse(chMatch.Groups[1].Value, out ch))
                    {
                        channel = ch;
                        System.Diagnostics.Debug.WriteLine($"[WiFi Interface] Channel (regex): {channel}");
                    }
                }
                continue;
            }

            // Radio type
            if (key == "radio type" || key.Contains("radio") || key.Contains("funktyp"))
            {
                radioType = value;
                System.Diagnostics.Debug.WriteLine($"[WiFi Interface] Radio type: {radioType}");
                continue;
            }

            // Band (newer Windows versions)
            if (key == "band" || key.EndsWith("band") || key.Contains("band") || key.Contains("频带"))
            {
                band = value;
                System.Diagnostics.Debug.WriteLine($"[WiFi Interface] Band: {band}");
                continue;
            }

            // Authentication
            if (key == "authentication" || key.Contains("authentication") || key.Contains("auth") || key.Contains("认证"))
            {
                security = value;
                continue;
            }

            // Receive/Transmit rate
            if (key.Contains("rate") || key.Contains("speed") || key.Contains("geschwindigkeit") || key.Contains("velocidad"))
            {
                var speedMatch = Regex.Match(value, @"(\d+(?:[.,]\d+)?)\s*(Mbps|Gbps)?", RegexOptions.IgnoreCase);
                if (speedMatch.Success)
                {
                    var rateStr = speedMatch.Groups[1].Value.Replace(',', '.');
                    if (double.TryParse(rateStr, System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var rate))
                    {
                        var rateInt = (int)rate;
                        if (speedMatch.Groups.Count > 2 &&
                            speedMatch.Groups[2].Value.Equals("Gbps", StringComparison.OrdinalIgnoreCase))
                            rateInt = (int)(rate * 1000);
                        speed = Math.Max(speed, rateInt);
                    }
                }
                continue;
            }
        }

        // Check if connected
        if (string.IsNullOrEmpty(ssid))
        {
            System.Diagnostics.Debug.WriteLine("[WiFi Interface] No SSID found");
            return null;
        }

        // Check state
        if (!string.IsNullOrEmpty(state) && state.ToLowerInvariant().Contains("disconnect"))
        {
            System.Diagnostics.Debug.WriteLine("[WiFi Interface] State indicates disconnected");
            return null;
        }

        // Determine band
        var determinedBand = band;
        if (string.IsNullOrEmpty(determinedBand) || determinedBand == "Unknown")
        {
            determinedBand = GetBandFromChannel(channel);
        }
        if (string.IsNullOrEmpty(determinedBand) || determinedBand == "Unknown")
        {
            determinedBand = GetBandFromRadioType(radioType);
        }

        System.Diagnostics.Debug.WriteLine($"[WiFi Interface] Final: SSID={ssid}, Channel={channel}, Band={determinedBand}, Signal={signal}");

        return new WiFiConnectionInfo
        {
            Ssid = ssid,
            Bssid = bssid,
            SignalStrength = signal > 0 ? signal : 75,
            Channel = channel,
            Security = !string.IsNullOrEmpty(security) ? security : "WPA2",
            LinkSpeed = speed,
            IpAddress = GetCurrentIpAddress(),
            IsConnected = true,
            Band = determinedBand,
            RadioType = radioType
        };
    }

    private static string? GetConnectedSsidFromProfile()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = "wlan show interfaces",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.GetEncoding(850) // OEM code page
            };

            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);

            // Look for SSID line (but not BSSID)
            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.Contains("BSSID", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = trimmed.Split(new[] { ':', '：' }, 2);
                    if (parts.Length > 1)
                    {
                        var ssid = parts[1].Trim();
                        if (!string.IsNullOrEmpty(ssid))
                            return ssid;
                    }
                }
            }
        }
        catch { }

        return null;
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
        {
            System.Diagnostics.Debug.WriteLine("[WiFi Parse] Empty output");
            return networks;
        }

        var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        System.Diagnostics.Debug.WriteLine($"[WiFi Parse] Processing {lines.Length} lines");

        string? currentSSID = null;
        string? currentBSSID = null;
        int signalPercent = 0;
        string networkType = "";
        string authentication = "";
        int channel = 0;
        string band = "";
        string radioType = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Extract key-value from line
            var colonIndex = line.IndexOfAny(new[] { ':', '：' });
            if (colonIndex <= 0) continue;

            var key = line.Substring(0, colonIndex).Trim().ToLowerInvariant();
            var value = line.Substring(colonIndex + 1).Trim();

            // Skip empty values
            if (string.IsNullOrEmpty(value)) continue;

            // Debug output for key lines
            if (key.Contains("channel") || key.Contains("ssid") || key.Contains("signal"))
            {
                System.Diagnostics.Debug.WriteLine($"[WiFi Parse] Key='{key}' Value='{value}'");
            }

            // Check for SSID (but not BSSID)
            if ((key == "ssid" || key.StartsWith("ssid ") || (key.Contains("ssid") && !key.Contains("bssid"))))
            {
                // Only process if this is truly SSID, not BSSID
                if (!key.Contains("bssid"))
                {
                    // Save previous network if exists
                    if (!string.IsNullOrEmpty(currentBSSID))
                    {
                        networks.Add(CreateNetworkInfo(currentSSID, currentBSSID, signalPercent,
                            channel, band, authentication, networkType, radioType, false));
                    }

                    currentSSID = value;
                    currentBSSID = null;
                    signalPercent = 0;
                    channel = 0;
                    band = "";
                    authentication = "";
                    networkType = "";
                    radioType = "";
                    System.Diagnostics.Debug.WriteLine($"[WiFi Parse] Found SSID: {currentSSID}");
                    continue;
                }
            }

            // Check for BSSID (MAC address pattern)
            if (key.Contains("bssid"))
            {
                // Save previous BSSID entry if exists (multiple BSSIDs per SSID)
                if (!string.IsNullOrEmpty(currentBSSID))
                {
                    networks.Add(CreateNetworkInfo(currentSSID, currentBSSID, signalPercent,
                        channel, band, authentication, networkType, radioType, false));
                    signalPercent = 0;
                    channel = 0;
                    band = "";
                    radioType = "";
                }

                // Try to extract MAC address from value
                var macMatch = Regex.Match(value, @"([0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2})");
                if (macMatch.Success)
                {
                    currentBSSID = macMatch.Groups[1].Value;
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    currentBSSID = value;
                }
                System.Diagnostics.Debug.WriteLine($"[WiFi Parse] Found BSSID: {currentBSSID}");
                continue;
            }

            // Check for Signal strength (look for percentage)
            if (key.Contains("signal") || key.Contains("信号") || key.Contains("señal") || key.Contains("stärke"))
            {
                var signalMatch = Regex.Match(value, @"(\d+)\s*%?");
                if (signalMatch.Success && int.TryParse(signalMatch.Groups[1].Value, out var sig))
                {
                    signalPercent = sig;
                    System.Diagnostics.Debug.WriteLine($"[WiFi Parse] Signal: {signalPercent}%");
                }
                continue;
            }

            // Check for Band directly
            if (key == "band" || key == "banda" || key == "频带" || key == "frequenzband")
            {
                band = value;
                System.Diagnostics.Debug.WriteLine($"[WiFi Parse] Band: {band}");
                continue;
            }

            // Check for Radio type (802.11ac, 802.11ax, etc.)
            if (key.Contains("radio") || key.Contains("tipo de radio") || key.Contains("funktyp") || key.Contains("无线电类型"))
            {
                radioType = value;
                // Also determine band from radio type if not yet set
                if (string.IsNullOrEmpty(band))
                {
                    band = GetBandFromRadioType(value);
                }
                System.Diagnostics.Debug.WriteLine($"[WiFi Parse] Radio type: {radioType}, Band from radio: {band}");
                continue;
            }

            // Check for Channel - be more permissive with matching
            if (key.Contains("channel") || key.Contains("kanal") || key.Contains("canal") || key.Contains("频道") || key.Contains("kanaal"))
            {
                if (int.TryParse(value, out var ch))
                {
                    channel = ch;
                    if (string.IsNullOrEmpty(band) || band == "Unknown")
                    {
                        band = GetBandFromChannel(channel);
                    }
                    System.Diagnostics.Debug.WriteLine($"[WiFi Parse] Channel: {channel}, Band from channel: {band}");
                }
                else
                {
                    var chMatch = Regex.Match(value, @"(\d+)");
                    if (chMatch.Success && int.TryParse(chMatch.Groups[1].Value, out ch))
                    {
                        channel = ch;
                        if (string.IsNullOrEmpty(band) || band == "Unknown")
                        {
                            band = GetBandFromChannel(channel);
                        }
                        System.Diagnostics.Debug.WriteLine($"[WiFi Parse] Channel (regex): {channel}, Band: {band}");
                    }
                }
                continue;
            }

            // Check for Network type
            if (key.Contains("network type") || key.Contains("tipo de red") || key.Contains("netzwerktyp") || key.Contains("type réseau"))
            {
                networkType = value;
                continue;
            }

            // Check for Authentication
            if (key.Contains("authentication") || key.Contains("authentifizierung") || key.Contains("autenticación") || key.Contains("认证") || key.Contains("authentification"))
            {
                authentication = value;
                continue;
            }
        }

        // Don't forget the last network
        if (!string.IsNullOrEmpty(currentBSSID))
        {
            networks.Add(CreateNetworkInfo(currentSSID, currentBSSID, signalPercent,
                channel, band, authentication, networkType, radioType, false));
        }

        System.Diagnostics.Debug.WriteLine($"[WiFi Parse] Total networks parsed: {networks.Count}");
        return networks;
    }

    private static WiFiNetworkInfo CreateNetworkInfo(string? ssid, string bssid, int signalPercent,
        int channel, string band, string authentication, string networkType, string radioType, bool isConnected)
    {
        var (quality, color) = GetSignalQuality(signalPercent);
        var bars = GetSignalBars(signalPercent);

        // Ensure band is determined
        var finalBand = band;
        if (string.IsNullOrEmpty(finalBand) || finalBand == "Unknown")
        {
            finalBand = GetBandFromChannel(channel);
        }
        if (string.IsNullOrEmpty(finalBand) || finalBand == "Unknown")
        {
            finalBand = GetBandFromRadioType(radioType);
        }

        return new WiFiNetworkInfo
        {
            Ssid = string.IsNullOrEmpty(ssid) ? "[Hidden Network]" : ssid,
            Bssid = bssid,
            SignalStrength = signalPercent,
            SignalBars = bars,
            SignalQuality = quality,
            SignalColor = color,
            Channel = channel,
            Band = finalBand,
            Security = authentication,
            IsSecured = !string.IsNullOrEmpty(authentication) && !authentication.Equals("Open", StringComparison.OrdinalIgnoreCase),
            IsConnected = isConnected,
            FrequencyMHz = GetFrequencyFromChannel(channel),
            NetworkType = !string.IsNullOrEmpty(radioType) ? radioType : networkType
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
        string band = "";
        string radioType = "";

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Use simple string extraction - more reliable than regex
            var colonIndex = line.IndexOfAny(new[] { ':', '：' });
            if (colonIndex <= 0) continue;

            var key = line.Substring(0, colonIndex).Trim().ToLowerInvariant();
            var value = line.Substring(colonIndex + 1).Trim();

            // Skip empty values
            if (string.IsNullOrEmpty(value)) continue;

            // Check state
            if (key.Contains("state") || key.Contains("état") || key.Contains("estado") || key.Contains("状态") || key.Contains("zustand"))
            {
                state = value;
                continue;
            }

            // Check SSID (but not BSSID) - key should be just "ssid" or start with "ssid" but not contain "bssid"
            if ((key == "ssid" || key.StartsWith("ssid ") || key == "nombre de red" || key.Contains("名称")) &&
                !key.Contains("bssid"))
            {
                if (!string.IsNullOrEmpty(value))
                {
                    ssid = value;
                }
                continue;
            }

            // Check BSSID
            if (key.Contains("bssid"))
            {
                // Try to find MAC address pattern anywhere in the value
                var macMatch = Regex.Match(value, @"([0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2}[:\-][0-9a-fA-F]{2})");
                if (macMatch.Success)
                {
                    bssid = macMatch.Groups[1].Value;
                }
                else if (!string.IsNullOrEmpty(value))
                {
                    bssid = value;
                }
                continue;
            }

            // Check Signal
            if (key.Contains("signal") || key.Contains("信号") || key.Contains("señal"))
            {
                var signalMatch = Regex.Match(value, @"(\d+)\s*%?");
                if (signalMatch.Success && int.TryParse(signalMatch.Groups[1].Value, out var sig))
                {
                    signal = sig;
                }
                continue;
            }

            // Check Band directly (newer Windows versions include this)
            if (key == "band" || key == "banda" || key == "频带" || key == "frequenzband")
            {
                band = value;
                continue;
            }

            // Check Radio type (802.11ac, 802.11ax, etc.)
            if (key.Contains("radio type") || key.Contains("tipo de radio") || key.Contains("funktyp") || key.Contains("无线电类型"))
            {
                radioType = value;
                continue;
            }

            // Check Channel
            if (key.Contains("channel") || key.Contains("kanal") || key.Contains("canal") || key.Contains("频道"))
            {
                if (int.TryParse(value, out var ch))
                {
                    channel = ch;
                }
                else
                {
                    // Try to extract just the number
                    var chMatch = Regex.Match(value, @"(\d+)");
                    if (chMatch.Success && int.TryParse(chMatch.Groups[1].Value, out ch))
                    {
                        channel = ch;
                    }
                }
                continue;
            }

            // Check Authentication/Security
            if (key.Contains("authentication") || key.Contains("authentifizierung") || key.Contains("autenticación") || key.Contains("认证"))
            {
                security = value;
                continue;
            }

            // Check Speed (Receive/Transmit rate)
            if (key.Contains("rate") || key.Contains("geschwindigkeit") || key.Contains("velocidad") || key.Contains("速率"))
            {
                var speedMatch = Regex.Match(value, @"(\d+(?:[.,]\d+)?)\s*(Mbps|Gbps)?", RegexOptions.IgnoreCase);
                if (speedMatch.Success)
                {
                    var rateStr = speedMatch.Groups[1].Value.Replace(',', '.');
                    if (double.TryParse(rateStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var rate))
                    {
                        var rateInt = (int)rate;
                        if (speedMatch.Groups.Count > 2 && speedMatch.Groups[2].Value.Equals("Gbps", StringComparison.OrdinalIgnoreCase))
                            rateInt = (int)(rate * 1000);
                        speed = Math.Max(speed, rateInt);
                    }
                }
                continue;
            }
        }

        // Check if actually connected
        if (string.IsNullOrEmpty(ssid))
            return null;

        // If state indicates disconnected, return null
        if (!string.IsNullOrEmpty(state))
        {
            var lowerState = state.ToLowerInvariant();
            if (lowerState.Contains("disconnected") || lowerState.Contains("getrennt") || lowerState.Contains("desconectado") || lowerState.Contains("déconnecté"))
            {
                return null;
            }
        }

        // Determine band from parsed value, channel, or radio type
        var determinedBand = band;
        if (string.IsNullOrEmpty(determinedBand) || determinedBand == "Unknown")
        {
            determinedBand = GetBandFromChannel(channel);
        }
        if (string.IsNullOrEmpty(determinedBand) || determinedBand == "Unknown")
        {
            determinedBand = GetBandFromRadioType(radioType);
        }

        return new WiFiConnectionInfo
        {
            Ssid = ssid,
            Bssid = bssid,
            SignalStrength = signal > 0 ? signal : 75, // Default to 75 if not found
            Channel = channel,
            Security = !string.IsNullOrEmpty(security) ? security : "WPA2",
            LinkSpeed = speed,
            IpAddress = GetCurrentIpAddress(),
            IsConnected = true,
            Band = determinedBand,
            RadioType = radioType
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
        if (channel <= 0) return "Unknown";
        return channel <= 14 ? "2.4 GHz" : "5 GHz";
    }

    private static string GetBandFromRadioType(string radioType)
    {
        if (string.IsNullOrEmpty(radioType)) return "Unknown";

        var lower = radioType.ToLowerInvariant();

        // 802.11ac and 802.11ax can be both bands, but ax is often 5GHz
        // 802.11a is 5 GHz only
        // 802.11b/g are 2.4 GHz only
        // 802.11n can be both
        if (lower.Contains("802.11a") && !lower.Contains("802.11ax"))
            return "5 GHz";
        if (lower.Contains("802.11b") || lower.Contains("802.11g"))
            return "2.4 GHz";

        // For ac/ax/n, we can't determine from radio type alone
        return "Unknown";
    }

    private static double GetFrequencyFromChannel(int channel)
    {
        if (channel <= 0)
        {
            return 0;
        }
        else if (channel <= 14)
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
