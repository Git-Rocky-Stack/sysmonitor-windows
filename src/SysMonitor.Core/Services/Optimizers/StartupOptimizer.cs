using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Optimizers;

/// <summary>
/// Lists what starts with Windows and turns entries on and off the way Windows does, so Task Manager and
/// Settings show the same thing this app does.
/// </summary>
/// <remarks>
/// Whether an entry may run is recorded under Explorer\StartupApproved, beside the entry's own key: twelve
/// bytes whose first byte is even when the entry may run and odd when it may not, followed by when it was
/// turned off. Task Manager writes 02 and 03; this machine also has 01 and 07, which are odd and so off.
/// Turning an entry off there leaves the entry itself in place, which is what makes it reversible.
/// See https://www.nutanix.com/en_sg/blog/windows-os-optimization-essentials-part-4-startup-items
/// </remarks>
public class StartupOptimizer : IStartupOptimizer
{
    private const byte ApprovedEnabled = 0x02;
    private const byte ApprovedDisabled = 0x03;
    private const string CurrentVersion = @"SOFTWARE\Microsoft\Windows\CurrentVersion";

    private readonly ILogger<StartupOptimizer> _logger;
    private readonly IReadOnlyList<StartupLocation> _locations;
    private readonly StartupFolder _startupFolder;

    public StartupOptimizer(ILogger<StartupOptimizer> logger)
        : this(logger, DefaultLocations(), DefaultStartupFolder())
    {
    }

    internal StartupOptimizer(ILogger<StartupOptimizer> logger, IReadOnlyList<StartupLocation> locations, StartupFolder startupFolder)
    {
        _logger = logger;
        _locations = locations;
        _startupFolder = startupFolder;
    }

    /// <summary>
    /// A place startup entries live. <paramref name="ApprovedPath"/> is the key that says whether each entry
    /// may run; RunOnce has no such key, and entries there are set aside in <paramref name="SetAsidePath"/>
    /// instead, where this app can still find and restore them.
    /// </summary>
    internal sealed record StartupLocation(RegistryKey Root, string Name, string RunPath, string? ApprovedPath, string SetAsidePath);

    /// <summary>The folder whose shortcuts start with Windows, and the key that says which of them may run.</summary>
    internal sealed record StartupFolder(string Path, RegistryKey ApprovalRoot, string ApprovalPath);

    internal static IReadOnlyList<StartupLocation> DefaultLocations() =>
    [
        new(Registry.CurrentUser, "HKCU Run", $@"{CurrentVersion}\Run", $@"{CurrentVersion}\Explorer\StartupApproved\Run", $@"{CurrentVersion}\Run-Disabled"),
        new(Registry.LocalMachine, "HKLM Run", $@"{CurrentVersion}\Run", $@"{CurrentVersion}\Explorer\StartupApproved\Run", $@"{CurrentVersion}\Run-Disabled"),
        new(Registry.CurrentUser, "HKCU RunOnce", $@"{CurrentVersion}\RunOnce", null, $@"{CurrentVersion}\RunOnce-Disabled"),
        new(Registry.LocalMachine, "HKLM RunOnce", $@"{CurrentVersion}\RunOnce", null, $@"{CurrentVersion}\RunOnce-Disabled"),
    ];

    private static StartupFolder DefaultStartupFolder() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        Registry.CurrentUser,
        $@"{CurrentVersion}\Explorer\StartupApproved\StartupFolder");

    public async Task<List<StartupItem>> GetStartupItemsAsync()
    {
        return await Task.Run(() =>
        {
            var items = new List<StartupItem>();

            foreach (var location in _locations)
            {
                AddRegistryItems(items, location);
                AddSetAsideItems(items, location);
            }

            AddStartupFolderItems(items);

            _logger.LogDebug("Found {Count} startup items, {Disabled} of them turned off",
                items.Count, items.Count(i => !i.IsEnabled));
            return items;
        });
    }

    public async Task<StartupChangeResult> EnableStartupItemAsync(StartupItem item) =>
        await Task.Run(() => Change(item, enable: true));

    public async Task<StartupChangeResult> DisableStartupItemAsync(StartupItem item) =>
        await Task.Run(() => Change(item, enable: false));

    public async Task<StartupChangeResult> DeleteStartupItemAsync(StartupItem item)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (item.Type == StartupItemType.StartupFolder)
                {
                    if (!File.Exists(item.FilePath))
                    {
                        return StartupChangeResult.Failed($"\"{item.Name}\" is no longer in the startup folder.");
                    }

                    File.Delete(item.FilePath);
                    RemoveApproval(_startupFolder.ApprovalRoot, _startupFolder.ApprovalPath, Path.GetFileName(item.FilePath));
                    _logger.LogInformation("Deleted startup folder item {Name}", item.Name);
                    return StartupChangeResult.Done($"\"{item.Name}\" was removed from the startup folder.");
                }

                var location = LocationOf(item);
                if (location == null)
                {
                    return StartupChangeResult.Failed($"\"{item.Name}\" is in a place this app does not manage ({item.Location}).");
                }

                var removed = DeleteValue(location.Root, location.RunPath, item.Name)
                              | DeleteValue(location.Root, location.SetAsidePath, item.Name);
                if (!removed)
                {
                    return StartupChangeResult.Failed($"\"{item.Name}\" is no longer listed under {location.Name}.");
                }

                if (location.ApprovedPath != null)
                {
                    RemoveApproval(location.Root, location.ApprovedPath, item.Name);
                }

                _logger.LogInformation("Deleted startup registry item {Name}", item.Name);
                return StartupChangeResult.Done($"\"{item.Name}\" was removed from {location.Name}.");
            }
            catch (UnauthorizedAccessException)
            {
                return NeedsAdministrator(item);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete startup item {Name}", item.Name);
                return StartupChangeResult.Failed($"\"{item.Name}\" could not be removed: {ex.Message}");
            }
        });
    }

    /// <summary>Whether an entry with this StartupApproved value may run. No value at all means it may.</summary>
    internal static bool MayRun(byte[]? approval) => approval is not { Length: > 0 } || (approval[0] & 1) == 0;

    /// <summary>The twelve bytes Windows writes for an entry that may or may not run.</summary>
    internal static byte[] ApprovalValue(bool enabled, DateTime disabledAtUtc)
    {
        var value = new byte[12];
        value[0] = enabled ? ApprovedEnabled : ApprovedDisabled;
        if (!enabled)
        {
            BitConverter.TryWriteBytes(value.AsSpan(4), disabledAtUtc.ToFileTimeUtc());
        }

        return value;
    }

    private StartupChangeResult Change(StartupItem item, bool enable)
    {
        var verb = enable ? "start" : "no longer start";
        try
        {
            if (item.Type == StartupItemType.StartupFolder)
            {
                if (!File.Exists(item.FilePath))
                {
                    return StartupChangeResult.Failed($"\"{item.Name}\" is no longer in the startup folder.");
                }

                WriteApproval(_startupFolder.ApprovalRoot, _startupFolder.ApprovalPath, Path.GetFileName(item.FilePath), enable);
                _logger.LogInformation("{Action} startup folder item {Name}", enable ? "Enabled" : "Disabled", item.Name);
                return StartupChangeResult.Done($"\"{item.Name}\" will {verb} with Windows.");
            }

            var location = LocationOf(item);
            if (location == null)
            {
                return StartupChangeResult.Failed($"\"{item.Name}\" is in a place this app does not manage ({item.Location}).");
            }

            var inRun = ValueExists(location.Root, location.RunPath, item.Name);
            var setAside = ValueExists(location.Root, location.SetAsidePath, item.Name);

            if (!inRun && !setAside)
            {
                return StartupChangeResult.Failed($"\"{item.Name}\" is no longer listed under {location.Name}.");
            }

            // An entry an older version of this app moved aside comes back to where Windows looks for it -
            // unless Windows already has one under that name. Then the live entry is the one that counts:
            // the program will have rewritten it, often with a new path after an update, and putting the
            // old copy back would point Windows at a version that is no longer installed. The stale copy is
            // dropped instead.
            if (enable && setAside)
            {
                if (inRun)
                {
                    DeleteValue(location.Root, location.SetAsidePath, item.Name);
                }
                else
                {
                    MoveValue(location.Root, location.SetAsidePath, location.RunPath, item.Name);
                }
            }

            if (location.ApprovedPath != null)
            {
                WriteApproval(location.Root, location.ApprovedPath, item.Name, enable);
            }
            else if (!enable && inRun)
            {
                // RunOnce has no approval key, so the entry is set aside where this app can restore it.
                MoveValue(location.Root, location.RunPath, location.SetAsidePath, item.Name);
            }

            _logger.LogInformation("{Action} startup item {Name}", enable ? "Enabled" : "Disabled", item.Name);
            return StartupChangeResult.Done($"\"{item.Name}\" will {verb} with Windows.");
        }
        catch (UnauthorizedAccessException)
        {
            return NeedsAdministrator(item);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to change startup item {Name}", item.Name);
            return StartupChangeResult.Failed($"\"{item.Name}\" could not be changed: {ex.Message}");
        }
    }

    private static StartupChangeResult NeedsAdministrator(StartupItem item) =>
        StartupChangeResult.Failed($"Changing \"{item.Name}\" needs administrator rights. Run SysMonitor as administrator and try again.");

    private StartupLocation? LocationOf(StartupItem item) =>
        _locations.FirstOrDefault(l => l.Name.Equals(item.Location, StringComparison.OrdinalIgnoreCase));

    private void AddRegistryItems(List<StartupItem> items, StartupLocation location)
    {
        try
        {
            using var key = location.Root.OpenSubKey(location.RunPath);
            if (key == null) return;

            using var approved = location.ApprovedPath == null ? null : location.Root.OpenSubKey(location.ApprovedPath);

            foreach (var valueName in key.GetValueNames())
            {
                try
                {
                    var command = key.GetValue(valueName)?.ToString() ?? "";
                    items.Add(new StartupItem
                    {
                        Name = valueName,
                        Command = command,
                        Location = location.Name,
                        Type = StartupItemType.Registry,
                        IsEnabled = MayRun(approved?.GetValue(valueName) as byte[]),
                        RegistryKey = $"{location.RunPath}\\{valueName}",
                        FilePath = ExtractFilePath(command),
                        Impact = EstimateImpact(command),
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogTrace(ex, "Failed to read startup value {ValueName}", valueName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read startup location {Location}", location.Name);
        }
    }

    /// <summary>Entries an older version of this app moved aside. They are off, and they are still listed.</summary>
    private void AddSetAsideItems(List<StartupItem> items, StartupLocation location)
    {
        try
        {
            using var key = location.Root.OpenSubKey(location.SetAsidePath);
            if (key == null) return;

            foreach (var valueName in key.GetValueNames())
            {
                if (items.Any(i => i.Location == location.Name && i.Name.Equals(valueName, StringComparison.OrdinalIgnoreCase)))
                    continue;

                var command = key.GetValue(valueName)?.ToString() ?? "";
                items.Add(new StartupItem
                {
                    Name = valueName,
                    Command = command,
                    Location = location.Name,
                    Type = StartupItemType.Registry,
                    IsEnabled = false,
                    RegistryKey = $"{location.SetAsidePath}\\{valueName}",
                    FilePath = ExtractFilePath(command),
                    Impact = EstimateImpact(command),
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read set-aside startup entries in {Location}", location.Name);
        }
    }

    private void AddStartupFolderItems(List<StartupItem> items)
    {
        if (!Directory.Exists(_startupFolder.Path)) return;

        using var approved = _startupFolder.ApprovalRoot.OpenSubKey(_startupFolder.ApprovalPath);

        foreach (var file in Directory.GetFiles(_startupFolder.Path, "*.lnk"))
        {
            try
            {
                items.Add(new StartupItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Command = file,
                    Location = "Startup Folder",
                    Type = StartupItemType.StartupFolder,
                    IsEnabled = MayRun(approved?.GetValue(Path.GetFileName(file)) as byte[]),
                    FilePath = file,
                    Impact = StartupImpact.Medium,
                });
            }
            catch (Exception ex)
            {
                _logger.LogTrace(ex, "Failed to read startup folder item {File}", file);
            }
        }
    }

    private static void WriteApproval(RegistryKey root, string approvedPath, string valueName, bool enabled)
    {
        using var key = root.CreateSubKey(approvedPath, writable: true)
            ?? throw new InvalidOperationException($"Could not open {approvedPath}.");
        key.SetValue(valueName, ApprovalValue(enabled, DateTime.UtcNow), RegistryValueKind.Binary);
    }

    private static void RemoveApproval(RegistryKey root, string approvedPath, string valueName)
    {
        using var key = root.OpenSubKey(approvedPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    private static bool ValueExists(RegistryKey root, string path, string valueName)
    {
        using var key = root.OpenSubKey(path);
        return key?.GetValue(valueName) != null;
    }

    private static bool DeleteValue(RegistryKey root, string path, string valueName)
    {
        using var key = root.OpenSubKey(path, writable: true);
        if (key?.GetValue(valueName) == null) return false;

        key.DeleteValue(valueName, throwOnMissingValue: false);
        return true;
    }

    private static void MoveValue(RegistryKey root, string fromPath, string toPath, string valueName)
    {
        using var from = root.OpenSubKey(fromPath, writable: true);
        var value = from?.GetValue(valueName);
        if (value == null) return;

        using (var to = root.CreateSubKey(toPath, writable: true))
        {
            to?.SetValue(valueName, value, from!.GetValueKind(valueName));
        }

        from!.DeleteValue(valueName, throwOnMissingValue: false);
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

/// <summary>What happened to a startup item, in words the user can be shown.</summary>
public sealed record StartupChangeResult(bool Success, string Message)
{
    public static StartupChangeResult Done(string message) => new(true, message);

    public static StartupChangeResult Failed(string message) => new(false, message);
}
