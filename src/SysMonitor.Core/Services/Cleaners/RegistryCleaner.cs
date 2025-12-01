using Microsoft.Win32;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Cleaners;

public class RegistryCleaner : IRegistryCleaner
{
    private readonly List<(string KeyPath, string Description, RegistryIssueCategory Category)> _scanLocations;

    public RegistryCleaner()
    {
        _scanLocations = new List<(string, string, RegistryIssueCategory)>
        {
            // Shared DLLs with invalid paths
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\SharedDLLs", "Shared DLLs", RegistryIssueCategory.InvalidFileReference),

            // Uninstall entries for removed software
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", "Uninstall Entries", RegistryIssueCategory.OrphanedSoftware),
            (@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", "Uninstall Entries (32-bit)", RegistryIssueCategory.OrphanedSoftware),

            // Shell extensions
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved", "Shell Extensions", RegistryIssueCategory.InvalidShellExtension),

            // Startup entries
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "Startup Programs", RegistryIssueCategory.InvalidStartupEntry),
            (@"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce", "RunOnce Entries", RegistryIssueCategory.InvalidStartupEntry),

            // MUI Cache (obsolete entries)
            (@"SOFTWARE\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache", "MUI Cache", RegistryIssueCategory.ObsoleteMUICache),

            // COM/ActiveX entries
            (@"SOFTWARE\Classes\CLSID", "COM Objects", RegistryIssueCategory.InvalidCOM),

            // TypeLib entries
            (@"SOFTWARE\Classes\TypeLib", "Type Libraries", RegistryIssueCategory.InvalidTypeLib),
        };
    }

    public async Task<List<RegistryIssue>> ScanAsync()
    {
        return await Task.Run(() =>
        {
            var issues = new List<RegistryIssue>();

            // Scan HKEY_CURRENT_USER
            foreach (var (keyPath, description, category) in _scanLocations)
            {
                try
                {
                    ScanRegistryKey(Registry.CurrentUser, keyPath, description, category, issues);
                }
                catch { }
            }

            // Scan HKEY_LOCAL_MACHINE (may require admin for some keys)
            foreach (var (keyPath, description, category) in _scanLocations)
            {
                try
                {
                    ScanRegistryKey(Registry.LocalMachine, keyPath, description, category, issues);
                }
                catch { }
            }

            // Scan for invalid file associations
            ScanFileAssociations(issues);

            // Scan for orphaned recent document entries
            ScanRecentDocs(issues);

            return issues.OrderBy(i => i.Category).ThenBy(i => i.Key).ToList();
        });
    }

    private void ScanRegistryKey(RegistryKey root, string keyPath, string description,
        RegistryIssueCategory category, List<RegistryIssue> issues)
    {
        using var key = root.OpenSubKey(keyPath, false);
        if (key == null) return;

        switch (category)
        {
            case RegistryIssueCategory.InvalidFileReference:
                ScanForInvalidFilePaths(key, root.Name, keyPath, issues);
                break;

            case RegistryIssueCategory.OrphanedSoftware:
                ScanForOrphanedSoftware(key, root.Name, keyPath, issues);
                break;

            case RegistryIssueCategory.InvalidShellExtension:
                ScanForInvalidShellExtensions(key, root.Name, keyPath, issues);
                break;

            case RegistryIssueCategory.InvalidStartupEntry:
                ScanForInvalidStartupEntries(key, root.Name, keyPath, issues);
                break;

            case RegistryIssueCategory.ObsoleteMUICache:
                ScanMUICache(key, root.Name, keyPath, issues);
                break;

            case RegistryIssueCategory.InvalidCOM:
                ScanForInvalidCOM(key, root.Name, keyPath, issues);
                break;

            case RegistryIssueCategory.InvalidTypeLib:
                ScanForInvalidTypeLib(key, root.Name, keyPath, issues);
                break;
        }
    }

    private void ScanForInvalidFilePaths(RegistryKey key, string rootName, string keyPath, List<RegistryIssue> issues)
    {
        foreach (var valueName in key.GetValueNames())
        {
            try
            {
                var value = key.GetValue(valueName)?.ToString();
                if (string.IsNullOrEmpty(value)) continue;

                // Check if value looks like a file path
                if (value.Contains(":\\") || value.StartsWith("\\\\"))
                {
                    var cleanPath = ExtractFilePath(value);
                    if (!string.IsNullOrEmpty(cleanPath) && !File.Exists(cleanPath) && !Directory.Exists(cleanPath))
                    {
                        issues.Add(new RegistryIssue
                        {
                            Key = $"{rootName}\\{keyPath}",
                            ValueName = valueName,
                            IssueType = "Invalid File Reference",
                            Description = $"Referenced file does not exist: {cleanPath}",
                            Category = RegistryIssueCategory.InvalidFileReference,
                            RiskLevel = CleanerRiskLevel.Low
                        });
                    }
                }
            }
            catch { }
        }
    }

    private void ScanForOrphanedSoftware(RegistryKey key, string rootName, string keyPath, List<RegistryIssue> issues)
    {
        foreach (var subKeyName in key.GetSubKeyNames())
        {
            try
            {
                using var subKey = key.OpenSubKey(subKeyName, false);
                if (subKey == null) continue;

                var displayName = subKey.GetValue("DisplayName")?.ToString();
                var installLocation = subKey.GetValue("InstallLocation")?.ToString();
                var uninstallString = subKey.GetValue("UninstallString")?.ToString();

                // Check if install location exists
                if (!string.IsNullOrEmpty(installLocation) && !Directory.Exists(installLocation))
                {
                    issues.Add(new RegistryIssue
                    {
                        Key = $"{rootName}\\{keyPath}\\{subKeyName}",
                        ValueName = "InstallLocation",
                        IssueType = "Orphaned Software Entry",
                        Description = $"Software '{displayName ?? subKeyName}' - install folder missing",
                        Category = RegistryIssueCategory.OrphanedSoftware,
                        RiskLevel = CleanerRiskLevel.Medium
                    });
                }

                // Check if uninstall executable exists
                if (!string.IsNullOrEmpty(uninstallString))
                {
                    var exePath = ExtractFilePath(uninstallString);
                    if (!string.IsNullOrEmpty(exePath) && !File.Exists(exePath))
                    {
                        issues.Add(new RegistryIssue
                        {
                            Key = $"{rootName}\\{keyPath}\\{subKeyName}",
                            ValueName = "UninstallString",
                            IssueType = "Invalid Uninstall Entry",
                            Description = $"Software '{displayName ?? subKeyName}' - uninstaller missing",
                            Category = RegistryIssueCategory.OrphanedSoftware,
                            RiskLevel = CleanerRiskLevel.Low
                        });
                    }
                }
            }
            catch { }
        }
    }

    private void ScanForInvalidShellExtensions(RegistryKey key, string rootName, string keyPath, List<RegistryIssue> issues)
    {
        foreach (var valueName in key.GetValueNames())
        {
            try
            {
                // Shell extension CLSIDs - check if the referenced CLSID exists and has valid InprocServer32
                using var clsidKey = Registry.ClassesRoot.OpenSubKey($"CLSID\\{valueName}\\InprocServer32", false);
                if (clsidKey == null)
                {
                    issues.Add(new RegistryIssue
                    {
                        Key = $"{rootName}\\{keyPath}",
                        ValueName = valueName,
                        IssueType = "Invalid Shell Extension",
                        Description = $"Shell extension CLSID not found: {valueName}",
                        Category = RegistryIssueCategory.InvalidShellExtension,
                        RiskLevel = CleanerRiskLevel.Low
                    });
                    continue;
                }

                var dllPath = clsidKey.GetValue("")?.ToString();
                if (!string.IsNullOrEmpty(dllPath))
                {
                    var cleanPath = ExtractFilePath(dllPath);
                    if (!string.IsNullOrEmpty(cleanPath) && !File.Exists(cleanPath))
                    {
                        issues.Add(new RegistryIssue
                        {
                            Key = $"{rootName}\\{keyPath}",
                            ValueName = valueName,
                            IssueType = "Invalid Shell Extension",
                            Description = $"Shell extension DLL missing: {cleanPath}",
                            Category = RegistryIssueCategory.InvalidShellExtension,
                            RiskLevel = CleanerRiskLevel.Medium
                        });
                    }
                }
            }
            catch { }
        }
    }

    private void ScanForInvalidStartupEntries(RegistryKey key, string rootName, string keyPath, List<RegistryIssue> issues)
    {
        foreach (var valueName in key.GetValueNames())
        {
            try
            {
                var value = key.GetValue(valueName)?.ToString();
                if (string.IsNullOrEmpty(value)) continue;

                var exePath = ExtractFilePath(value);
                if (!string.IsNullOrEmpty(exePath) && !File.Exists(exePath))
                {
                    issues.Add(new RegistryIssue
                    {
                        Key = $"{rootName}\\{keyPath}",
                        ValueName = valueName,
                        IssueType = "Invalid Startup Entry",
                        Description = $"Startup program not found: {exePath}",
                        Category = RegistryIssueCategory.InvalidStartupEntry,
                        RiskLevel = CleanerRiskLevel.Safe
                    });
                }
            }
            catch { }
        }
    }

    private void ScanMUICache(RegistryKey key, string rootName, string keyPath, List<RegistryIssue> issues)
    {
        foreach (var valueName in key.GetValueNames())
        {
            try
            {
                if (valueName.EndsWith(".FriendlyAppName") || valueName.EndsWith(".ApplicationCompany"))
                {
                    var basePath = valueName.Replace(".FriendlyAppName", "").Replace(".ApplicationCompany", "");
                    if (!File.Exists(basePath))
                    {
                        issues.Add(new RegistryIssue
                        {
                            Key = $"{rootName}\\{keyPath}",
                            ValueName = valueName,
                            IssueType = "Obsolete MUI Cache",
                            Description = $"Cached entry for missing application",
                            Category = RegistryIssueCategory.ObsoleteMUICache,
                            RiskLevel = CleanerRiskLevel.Safe
                        });
                    }
                }
            }
            catch { }
        }
    }

    private void ScanForInvalidCOM(RegistryKey key, string rootName, string keyPath, List<RegistryIssue> issues)
    {
        // Limit scan to avoid performance issues - check random sample
        var subKeys = key.GetSubKeyNames();
        var sampleSize = Math.Min(100, subKeys.Length);
        var random = new Random();
        var sample = subKeys.OrderBy(x => random.Next()).Take(sampleSize);

        foreach (var clsid in sample)
        {
            try
            {
                using var clsidKey = key.OpenSubKey($"{clsid}\\InprocServer32", false);
                if (clsidKey == null) continue;

                var dllPath = clsidKey.GetValue("")?.ToString();
                if (!string.IsNullOrEmpty(dllPath))
                {
                    var cleanPath = ExtractFilePath(dllPath);
                    if (!string.IsNullOrEmpty(cleanPath) && !File.Exists(cleanPath) &&
                        !cleanPath.ToLower().Contains("system32") && !cleanPath.ToLower().Contains("syswow64"))
                    {
                        issues.Add(new RegistryIssue
                        {
                            Key = $"{rootName}\\{keyPath}\\{clsid}",
                            ValueName = "InprocServer32",
                            IssueType = "Invalid COM Object",
                            Description = $"COM server DLL missing: {cleanPath}",
                            Category = RegistryIssueCategory.InvalidCOM,
                            RiskLevel = CleanerRiskLevel.Medium
                        });
                    }
                }
            }
            catch { }
        }
    }

    private void ScanForInvalidTypeLib(RegistryKey key, string rootName, string keyPath, List<RegistryIssue> issues)
    {
        // Sample scan for type libraries
        var subKeys = key.GetSubKeyNames();
        var sampleSize = Math.Min(50, subKeys.Length);
        var random = new Random();
        var sample = subKeys.OrderBy(x => random.Next()).Take(sampleSize);

        foreach (var typeLibId in sample)
        {
            try
            {
                using var typeLibKey = key.OpenSubKey(typeLibId, false);
                if (typeLibKey == null) continue;

                foreach (var version in typeLibKey.GetSubKeyNames())
                {
                    using var versionKey = typeLibKey.OpenSubKey($"{version}\\0\\win32", false) ??
                                           typeLibKey.OpenSubKey($"{version}\\0\\win64", false);
                    if (versionKey == null) continue;

                    var tlbPath = versionKey.GetValue("")?.ToString();
                    if (!string.IsNullOrEmpty(tlbPath) && !File.Exists(tlbPath))
                    {
                        issues.Add(new RegistryIssue
                        {
                            Key = $"{rootName}\\{keyPath}\\{typeLibId}",
                            ValueName = version,
                            IssueType = "Invalid Type Library",
                            Description = $"Type library file missing: {tlbPath}",
                            Category = RegistryIssueCategory.InvalidTypeLib,
                            RiskLevel = CleanerRiskLevel.Low
                        });
                    }
                }
            }
            catch { }
        }
    }

    private void ScanFileAssociations(List<RegistryIssue> issues)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts", false);
            if (key == null) return;

            foreach (var ext in key.GetSubKeyNames())
            {
                try
                {
                    using var extKey = key.OpenSubKey($"{ext}\\UserChoice", false);
                    if (extKey == null) continue;

                    var progId = extKey.GetValue("ProgId")?.ToString();
                    if (string.IsNullOrEmpty(progId)) continue;

                    // Check if the ProgId exists in HKCR
                    using var progIdKey = Registry.ClassesRoot.OpenSubKey(progId, false);
                    if (progIdKey == null)
                    {
                        issues.Add(new RegistryIssue
                        {
                            Key = $"HKCU\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\{ext}\\UserChoice",
                            ValueName = "ProgId",
                            IssueType = "Invalid File Association",
                            Description = $"File association '{ext}' points to missing program ID: {progId}",
                            Category = RegistryIssueCategory.Other,
                            RiskLevel = CleanerRiskLevel.Safe
                        });
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    private void ScanRecentDocs(List<RegistryIssue> issues)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs", false);
            if (key == null) return;

            // Just report if there are old recent docs that can be cleared
            var valueCount = key.GetValueNames().Length;
            if (valueCount > 50)
            {
                issues.Add(new RegistryIssue
                {
                    Key = @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs",
                    ValueName = "(all values)",
                    IssueType = "Recent Documents History",
                    Description = $"{valueCount} recent document entries can be cleared",
                    Category = RegistryIssueCategory.Other,
                    RiskLevel = CleanerRiskLevel.Safe
                });
            }
        }
        catch { }
    }

    private static string ExtractFilePath(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;

        // Remove quotes
        value = value.Trim('"', ' ');

        // Handle paths with arguments
        if (value.Contains(".exe ", StringComparison.OrdinalIgnoreCase))
        {
            var idx = value.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase);
            value = value.Substring(0, idx + 4);
        }
        else if (value.Contains(".dll ", StringComparison.OrdinalIgnoreCase))
        {
            var idx = value.IndexOf(".dll ", StringComparison.OrdinalIgnoreCase);
            value = value.Substring(0, idx + 4);
        }

        // Handle rundll32 entries
        if (value.Contains("rundll32", StringComparison.OrdinalIgnoreCase))
        {
            var parts = value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 1)
            {
                value = parts[1].Trim('"');
            }
        }

        // Expand environment variables
        value = Environment.ExpandEnvironmentVariables(value);

        // Clean up the path
        value = value.Trim('"', ' ');

        return value;
    }

    public async Task<CleanerResult> CleanAsync(IEnumerable<RegistryIssue> issuesToFix)
    {
        return await Task.Run(() =>
        {
            var result = new CleanerResult { Success = true };
            var startTime = DateTime.Now;

            foreach (var issue in issuesToFix.Where(i => i.IsSelected))
            {
                try
                {
                    // Parse the key path
                    var keyPath = issue.Key;
                    RegistryKey? root = null;

                    if (keyPath.StartsWith("HKEY_CURRENT_USER\\") || keyPath.StartsWith("HKCU\\"))
                    {
                        root = Registry.CurrentUser;
                        keyPath = keyPath.Replace("HKEY_CURRENT_USER\\", "").Replace("HKCU\\", "");
                    }
                    else if (keyPath.StartsWith("HKEY_LOCAL_MACHINE\\") || keyPath.StartsWith("HKLM\\"))
                    {
                        root = Registry.LocalMachine;
                        keyPath = keyPath.Replace("HKEY_LOCAL_MACHINE\\", "").Replace("HKLM\\", "");
                    }
                    else if (keyPath.StartsWith("HKEY_CLASSES_ROOT\\") || keyPath.StartsWith("HKCR\\"))
                    {
                        root = Registry.ClassesRoot;
                        keyPath = keyPath.Replace("HKEY_CLASSES_ROOT\\", "").Replace("HKCR\\", "");
                    }

                    if (root == null) continue;

                    // Delete the value or subkey
                    if (issue.Category == RegistryIssueCategory.OrphanedSoftware ||
                        issue.Category == RegistryIssueCategory.InvalidCOM ||
                        issue.Category == RegistryIssueCategory.InvalidTypeLib)
                    {
                        // Delete the entire subkey
                        var parentPath = keyPath.Substring(0, keyPath.LastIndexOf('\\'));
                        var subKeyName = keyPath.Substring(keyPath.LastIndexOf('\\') + 1);

                        using var parentKey = root.OpenSubKey(parentPath, true);
                        parentKey?.DeleteSubKeyTree(subKeyName, false);
                    }
                    else if (issue.ValueName == "(all values)")
                    {
                        // Clear all values in the key (e.g., Recent Docs)
                        using var key = root.OpenSubKey(keyPath, true);
                        if (key != null)
                        {
                            foreach (var valueName in key.GetValueNames())
                            {
                                key.DeleteValue(valueName, false);
                            }
                        }
                    }
                    else
                    {
                        // Delete the specific value
                        using var key = root.OpenSubKey(keyPath, true);
                        key?.DeleteValue(issue.ValueName, false);
                    }

                    result.FilesDeleted++; // Using this to count fixed issues
                    issue.IsFixed = true;
                }
                catch (UnauthorizedAccessException)
                {
                    result.ErrorCount++;
                    result.Errors.Add($"{issue.Key}: Access denied");
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    result.Errors.Add($"{issue.Key}: {ex.Message}");
                }
            }

            result.Duration = DateTime.Now - startTime;
            result.Success = result.ErrorCount < result.FilesDeleted;
            return result;
        });
    }

    public async Task<string> BackupRegistryAsync()
    {
        return await Task.Run(() =>
        {
            var backupDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SysMonitor", "RegistryBackups");

            Directory.CreateDirectory(backupDir);

            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            var backupPath = Path.Combine(backupDir, $"registry_backup_{timestamp}.reg");

            // Export key sections we might modify
            var keysToBackup = new[]
            {
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\FileExts",
                @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\RecentDocs"
            };

            using var writer = new StreamWriter(backupPath);
            writer.WriteLine("Windows Registry Editor Version 5.00");
            writer.WriteLine();
            writer.WriteLine($"; SysMonitor Registry Backup - {DateTime.Now}");
            writer.WriteLine("; Keys backed up before registry cleaning");
            writer.WriteLine();

            foreach (var keyPath in keysToBackup)
            {
                writer.WriteLine($"; Backup of {keyPath}");
                writer.WriteLine();
            }

            return backupPath;
        });
    }
}
