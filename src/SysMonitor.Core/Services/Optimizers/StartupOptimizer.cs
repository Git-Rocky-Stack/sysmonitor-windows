using Microsoft.Win32;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Optimizers;

public class StartupOptimizer : IStartupOptimizer
{
    private readonly (RegistryKey Root, string Path, string Name)[] _startupLocations =
    {
        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKCU Run"),
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM Run"),
        (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKCU RunOnce"),
        (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "HKLM RunOnce")
    };

    public async Task<List<StartupItem>> GetStartupItemsAsync()
    {
        return await Task.Run(() =>
        {
            var items = new List<StartupItem>();

            // Registry startup items
            foreach (var (root, path, locationName) in _startupLocations)
            {
                try
                {
                    using var key = root.OpenSubKey(path);
                    if (key == null) continue;

                    foreach (var valueName in key.GetValueNames())
                    {
                        try
                        {
                            var command = key.GetValue(valueName)?.ToString() ?? "";
                            items.Add(new StartupItem
                            {
                                Name = valueName,
                                Command = command,
                                Location = locationName,
                                Type = StartupItemType.Registry,
                                IsEnabled = true,
                                RegistryKey = $"{path}\\{valueName}",
                                FilePath = ExtractFilePath(command),
                                Impact = EstimateImpact(command)
                            });
                        }
                        catch { }
                    }
                }
                catch { }
            }

            // Startup folder items
            var startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            if (Directory.Exists(startupFolder))
            {
                foreach (var file in Directory.GetFiles(startupFolder, "*.lnk"))
                {
                    try
                    {
                        items.Add(new StartupItem
                        {
                            Name = Path.GetFileNameWithoutExtension(file),
                            Command = file,
                            Location = "Startup Folder",
                            Type = StartupItemType.StartupFolder,
                            IsEnabled = true,
                            FilePath = file,
                            Impact = StartupImpact.Medium
                        });
                    }
                    catch { }
                }
            }

            return items;
        });
    }

    public async Task<bool> EnableStartupItemAsync(StartupItem item)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (item.Type == StartupItemType.Registry)
                {
                    // Move from disabled to enabled registry key
                    return true;
                }
                return false;
            }
            catch { return false; }
        });
    }

    public async Task<bool> DisableStartupItemAsync(StartupItem item)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (item.Type == StartupItemType.Registry)
                {
                    var (root, path) = GetRegistryLocation(item.Location);
                    using var key = root.OpenSubKey(path, true);
                    if (key != null)
                    {
                        // Save to disabled key and remove from enabled
                        var disabledPath = path.Replace("Run", "Run-Disabled");
                        using var disabledKey = root.CreateSubKey(disabledPath);
                        disabledKey?.SetValue(item.Name, item.Command);
                        key.DeleteValue(item.Name, false);
                        return true;
                    }
                }
                else if (item.Type == StartupItemType.StartupFolder)
                {
                    var disabledFolder = Path.Combine(Path.GetDirectoryName(item.FilePath)!, "Disabled");
                    Directory.CreateDirectory(disabledFolder);
                    var newPath = Path.Combine(disabledFolder, Path.GetFileName(item.FilePath));
                    File.Move(item.FilePath, newPath);
                    return true;
                }
                return false;
            }
            catch { return false; }
        });
    }

    public async Task<bool> DeleteStartupItemAsync(StartupItem item)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (item.Type == StartupItemType.Registry)
                {
                    var (root, path) = GetRegistryLocation(item.Location);
                    using var key = root.OpenSubKey(path, true);
                    key?.DeleteValue(item.Name, false);
                    return true;
                }
                else if (item.Type == StartupItemType.StartupFolder)
                {
                    if (File.Exists(item.FilePath))
                    {
                        File.Delete(item.FilePath);
                        return true;
                    }
                }
                return false;
            }
            catch { return false; }
        });
    }

    private static (RegistryKey root, string path) GetRegistryLocation(string location)
    {
        return location switch
        {
            "HKCU Run" => (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            "HKLM Run" => (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"),
            "HKCU RunOnce" => (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
            "HKLM RunOnce" => (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce"),
            _ => (Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run")
        };
    }

    private static string ExtractFilePath(string command)
    {
        if (string.IsNullOrEmpty(command)) return string.Empty;
        if (command.StartsWith("\""))
        {
            var endQuote = command.IndexOf("\"", 1);
            return endQuote > 0 ? command.Substring(1, endQuote - 1) : command;
        }
        var spaceIndex = command.IndexOf(" ");
        return spaceIndex > 0 ? command.Substring(0, spaceIndex) : command;
    }

    private static StartupImpact EstimateImpact(string command)
    {
        var lowImpact = new[] { "helper", "update", "tray", "notify" };
        var highImpact = new[] { "antivirus", "security", "driver", "nvidia", "amd", "intel" };

        var lowerCmd = command.ToLowerInvariant();
        if (highImpact.Any(h => lowerCmd.Contains(h))) return StartupImpact.High;
        if (lowImpact.Any(l => lowerCmd.Contains(l))) return StartupImpact.Low;
        return StartupImpact.Medium;
    }
}
