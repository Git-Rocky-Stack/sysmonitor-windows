using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Cleaners;

public class RegistryCleaner : IRegistryCleaner
{
    private readonly ILogger _logger;

    private readonly List<(string KeyPath, string Description, RegistryIssueCategory Category)> _scanLocations;
    private readonly string _backupFolder;

    public RegistryCleaner(ILogger<RegistryCleaner>? logger = null)
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "RegistryBackups"), logger)
    {
    }

    /// <summary>Uses <paramref name="backupFolder"/> for registry backups (tests use an isolated folder).</summary>
    internal RegistryCleaner(string backupFolder, ILogger<RegistryCleaner>? logger = null)
    {
        _logger = logger ?? NullLogger<RegistryCleaner>.Instance;
        _backupFolder = backupFolder;
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

    public async Task<List<RegistryIssue>> ScanAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var issues = new List<RegistryIssue>();

            // Checked between locations rather than inside a key: one key is quick, the whole walk is not.
            // The catch below is for a location that will not open, and must not swallow the cancellation.
            cancellationToken.ThrowIfCancellationRequested();

            // Scan HKEY_CURRENT_USER
            foreach (var (keyPath, description, category) in _scanLocations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    ScanRegistryKey(Registry.CurrentUser, keyPath, description, category, issues);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "ScanAsync failed");
                }
            }

            // Scan HKEY_LOCAL_MACHINE (may require admin for some keys)
            foreach (var (keyPath, description, category) in _scanLocations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    ScanRegistryKey(Registry.LocalMachine, keyPath, description, category, issues);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "ScanAsync failed");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            // Scan for invalid file associations
            ScanFileAssociations(issues);

            cancellationToken.ThrowIfCancellationRequested();

            // Scan for orphaned recent document entries
            ScanRecentDocs(issues);

            // Check protection status for all issues
            foreach (var issue in issues)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CheckProtectionStatus(issue);
            }

            return issues.OrderBy(i => i.IsProtected).ThenBy(i => i.Category).ThenBy(i => i.Key).ToList();
        });
    }

    private void CheckProtectionStatus(RegistryIssue issue)
    {
        try
        {
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

            if (root == null)
            {
                issue.IsProtected = true;
                issue.ProtectionReason = "Unknown registry root";
                issue.IsSelected = false;
                return;
            }

            // For subkey deletions, check the parent key
            if (issue.Category == RegistryIssueCategory.OrphanedSoftware ||
                issue.Category == RegistryIssueCategory.InvalidCOM ||
                issue.Category == RegistryIssueCategory.InvalidTypeLib)
            {
                var lastBackslash = keyPath.LastIndexOf('\\');
                if (lastBackslash <= 0)
                {
                    issue.IsProtected = true;
                    issue.ProtectionReason = "Invalid key path";
                    issue.IsSelected = false;
                    return;
                }

                var parentPath = keyPath.Substring(0, lastBackslash);
                using var parentKey = root.OpenSubKey(parentPath, writable: true);
                if (parentKey == null)
                {
                    issue.IsProtected = true;
                    issue.ProtectionReason = "Protected by Windows (TrustedInstaller)";
                    issue.IsSelected = false;
                }
            }
            else
            {
                // For value deletions, check the key itself
                using var key = root.OpenSubKey(keyPath, writable: true);
                if (key == null)
                {
                    issue.IsProtected = true;
                    issue.ProtectionReason = "Protected by Windows (TrustedInstaller)";
                    issue.IsSelected = false;
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            issue.IsProtected = true;
            issue.ProtectionReason = "Access denied (requires elevation)";
            issue.IsSelected = false;
        }
        catch (System.Security.SecurityException)
        {
            issue.IsProtected = true;
            issue.ProtectionReason = "Protected by security policy";
            issue.IsSelected = false;
        }
        catch
        {
            // If we can't determine, assume it might be protected
            issue.IsProtected = true;
            issue.ProtectionReason = "Unable to verify access";
            issue.IsSelected = false;
        }
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
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ScanForInvalidFilePaths failed");
            }
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
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ScanForOrphanedSoftware failed");
            }
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
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ScanForInvalidShellExtensions failed");
            }
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
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ScanForInvalidStartupEntries failed");
            }
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
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ScanMUICache failed");
            }
        }
    }

    private void ScanForInvalidCOM(RegistryKey key, string rootName, string keyPath, List<RegistryIssue> issues)
    {
        // Scan all COM objects (no sampling - we need consistent results)
        var subKeys = key.GetSubKeyNames();

        foreach (var clsid in subKeys)
        {
            try
            {
                using var clsidKey = key.OpenSubKey($"{clsid}\\InprocServer32", false);
                if (clsidKey == null) continue;

                var dllPath = clsidKey.GetValue("")?.ToString();
                if (!string.IsNullOrEmpty(dllPath) && IsMissingComServer(dllPath))
                {
                    issues.Add(new RegistryIssue
                    {
                        Key = $"{rootName}\\{keyPath}\\{clsid}",
                        ValueName = "InprocServer32",
                        IssueType = "Invalid COM Object",
                        Description = $"COM server DLL missing: {ExtractFilePath(dllPath)}",
                        Category = RegistryIssueCategory.InvalidCOM,
                        RiskLevel = CleanerRiskLevel.Medium
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ScanForInvalidCOM failed");
            }
        }
    }

    private void ScanForInvalidTypeLib(RegistryKey key, string rootName, string keyPath, List<RegistryIssue> issues)
    {
        // Scan all type libraries (no sampling - we need consistent results)
        var subKeys = key.GetSubKeyNames();

        foreach (var typeLibId in subKeys)
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
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ScanForInvalidTypeLib failed");
            }
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
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "ScanFileAssociations failed");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ScanFileAssociations failed");
        }
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ScanRecentDocs failed");
        }
    }

    /// <summary>
    /// Whether a registered COM server's file can be shown to be gone.
    /// <para>
    /// Only a fully qualified path can. COM servers are commonly registered by bare name - mapi32.dll,
    /// mscoree.dll - and Windows finds those through the DLL search order, which includes directories this
    /// scan has no way to enumerate. <c>File.Exists</c> on a bare name resolves it against the current
    /// directory of this process instead, and answers false for a file that is sitting in System32. The
    /// consequence of believing that answer is deleting the CLSID subtree of a working component.
    /// </para>
    /// <para>
    /// So: a rooted path is checked, and anything else is left alone. A cleaner that cannot prove absence
    /// must not act on a guess.
    /// </para>
    /// </summary>
    internal static bool IsMissingComServer(string registeredValue)
    {
        var path = ExtractFilePath(registeredValue);
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (!Path.IsPathFullyQualified(path))
            return false;

        // Windows' own files are left to Windows, as they always have been.
        if (path.Contains("system32", StringComparison.OrdinalIgnoreCase) ||
            path.Contains("syswow64", StringComparison.OrdinalIgnoreCase))
            return false;

        return !File.Exists(path);
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

    public async Task<CleanerResult> CleanAsync(IEnumerable<RegistryIssue> issuesToFix, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var result = new CleanerResult { Success = true };
            var startTime = DateTime.Now;

            cancellationToken.ThrowIfCancellationRequested();

            var issuesToProcess = issuesToFix.Where(i => i.IsSelected).ToList();

            // Between entries, never part way through one: a half-applied registry change is worse than
            // one that was not started.
            foreach (var issue in issuesToProcess)
            {
                cancellationToken.ThrowIfCancellationRequested();
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

                    if (root == null)
                    {
                        result.ErrorCount++;
                        result.Errors.Add($"{issue.Key}: Unknown registry root");
                        continue;
                    }

                    bool operationSucceeded = false;

                    // Delete the value or subkey
                    if (issue.Category == RegistryIssueCategory.OrphanedSoftware ||
                        issue.Category == RegistryIssueCategory.InvalidCOM ||
                        issue.Category == RegistryIssueCategory.InvalidTypeLib)
                    {
                        // Delete the entire subkey
                        var lastBackslash = keyPath.LastIndexOf('\\');
                        if (lastBackslash <= 0)
                        {
                            result.ErrorCount++;
                            result.Errors.Add($"{issue.Key}: Invalid key path structure");
                            continue;
                        }

                        var parentPath = keyPath.Substring(0, lastBackslash);
                        var subKeyName = keyPath.Substring(lastBackslash + 1);

                        using var parentKey = root.OpenSubKey(parentPath, writable: true);
                        if (parentKey != null)
                        {
                            // Verify subkey exists before trying to delete
                            var subKeyExists = parentKey.GetSubKeyNames().Contains(subKeyName, StringComparer.OrdinalIgnoreCase);
                            if (subKeyExists)
                            {
                                parentKey.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);

                                // Verify deletion succeeded
                                var stillExists = parentKey.GetSubKeyNames().Contains(subKeyName, StringComparer.OrdinalIgnoreCase);
                                if (!stillExists)
                                {
                                    operationSucceeded = true;
                                }
                                else
                                {
                                    result.ErrorCount++;
                                    result.Errors.Add($"{issue.Key}: Failed to delete subkey (may be protected)");
                                }
                            }
                            else
                            {
                                // Already deleted or doesn't exist
                                operationSucceeded = true;
                            }
                        }
                        else
                        {
                            result.ErrorCount++;
                            result.Errors.Add($"{issue.Key}: Cannot open parent key for writing (requires admin)");
                        }
                    }
                    else if (issue.ValueName == "(all values)")
                    {
                        // Clear all values in the key (e.g., Recent Docs)
                        using var key = root.OpenSubKey(keyPath, writable: true);
                        if (key != null)
                        {
                            var valueNames = key.GetValueNames().ToList();
                            int deletedCount = 0;
                            foreach (var valueName in valueNames)
                            {
                                try
                                {
                                    key.DeleteValue(valueName, throwOnMissingValue: false);
                                    // Verify deletion
                                    if (key.GetValue(valueName) == null)
                                    {
                                        deletedCount++;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogDebug(ex, "CleanAsync failed");
                                }
                            }
                            operationSucceeded = deletedCount > 0;
                            if (deletedCount < valueNames.Count)
                            {
                                result.Errors.Add($"{issue.Key}: Deleted {deletedCount}/{valueNames.Count} values");
                            }
                        }
                        else
                        {
                            result.ErrorCount++;
                            result.Errors.Add($"{issue.Key}: Cannot open key for writing (requires admin)");
                        }
                    }
                    else
                    {
                        // Delete the specific value
                        using var key = root.OpenSubKey(keyPath, writable: true);
                        if (key != null)
                        {
                            // Check if value exists before trying to delete
                            var valueExists = key.GetValueNames().Contains(issue.ValueName, StringComparer.OrdinalIgnoreCase);
                            if (valueExists)
                            {
                                key.DeleteValue(issue.ValueName, throwOnMissingValue: false);

                                // Verify deletion succeeded
                                var stillExists = key.GetValueNames().Contains(issue.ValueName, StringComparer.OrdinalIgnoreCase);
                                if (!stillExists)
                                {
                                    operationSucceeded = true;
                                }
                                else
                                {
                                    result.ErrorCount++;
                                    result.Errors.Add($"{issue.Key}\\{issue.ValueName}: Failed to delete value");
                                }
                            }
                            else
                            {
                                // Already deleted or doesn't exist
                                operationSucceeded = true;
                            }
                        }
                        else
                        {
                            result.ErrorCount++;
                            result.Errors.Add($"{issue.Key}: Cannot open key for writing (requires admin)");
                        }
                    }

                    if (operationSucceeded)
                    {
                        result.FilesDeleted++; // Using this to count fixed issues
                        issue.IsFixed = true;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    result.ErrorCount++;
                    result.Errors.Add($"{issue.Key}: Access denied (requires admin)");
                }
                catch (System.Security.SecurityException)
                {
                    result.ErrorCount++;
                    result.Errors.Add($"{issue.Key}: Security exception (requires admin)");
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    result.Errors.Add($"{issue.Key}: {ex.Message}");
                }
            }

            result.Duration = DateTime.Now - startTime;
            result.Success = result.FilesDeleted > 0;
            return result;
        });
    }

    public string BackupFolder => _backupFolder;

    /// <summary>
    /// The file holding the SHA-256 of a backup, written beside it when the backup is made.
    /// <para>
    /// This does not stop a process running as this user from writing both files - nothing stored in this
    /// user's profile can. What it does is make the app refuse anything it did not write itself, which is
    /// the policy "Restore last backup" already implies, and which closes the drop-a-file-and-wait route
    /// into HKLM. A backup that must survive a hostile local process belongs somewhere only administrators
    /// can write.
    /// </para>
    /// </summary>
    private static string FingerprintPathFor(string backupPath) => backupPath + ".sha256";

    private static async Task<string> Sha256OfAsync(Stream stream)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(await sha.ComputeHashAsync(stream));
    }

    private async Task RecordFingerprintAsync(string backupPath)
    {
        try
        {
            await using var file = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            await File.WriteAllTextAsync(FingerprintPathFor(backupPath), await Sha256OfAsync(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The backup itself is written and usable by hand; only the app's own restore will refuse it.
            _logger.LogWarning(ex, "Could not record the fingerprint for {BackupPath}", backupPath);
        }
    }

    private async Task<bool> MatchesRecordedFingerprintAsync(string backupPath, Stream openBackup)
    {
        var fingerprintPath = FingerprintPathFor(backupPath);
        if (!File.Exists(fingerprintPath))
            return false;

        try
        {
            var recorded = (await File.ReadAllTextAsync(fingerprintPath)).Trim();
            var actual = await Sha256OfAsync(openBackup);
            return recorded.Equals(actual, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read the fingerprint for {BackupPath}", backupPath);
            return false;
        }
    }

    public IReadOnlyList<string> GetBackups() =>
        Directory.Exists(_backupFolder)
            ? new DirectoryInfo(_backupFolder).GetFiles("registry_backup_*.reg")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .ToList()
            : [];

    private const string RegFileHeader = "Windows Registry Editor Version 5.00";
    private static readonly string[] RegistryRootPrefixes =
    [
        "HKEY_CURRENT_USER\\", "HKCU\\", "HKEY_LOCAL_MACHINE\\", "HKLM\\", "HKEY_CLASSES_ROOT\\", "HKCR\\"
    ];

    public async Task<RegistryBackupResult> BackupRegistryAsync(IEnumerable<RegistryIssue> issuesToFix)
    {
        // Every fix deletes either the issue's key with its whole subtree, or values inside the key, so exporting
        // each issue's key captures everything the run can change. Each key is exported on its own: reg.exe
        // exits 0 while silently omitting subkeys it cannot read, so exporting a parent would hide a failure.
        var keys = issuesToFix
            .Where(i => i.IsSelected && RegistryRootPrefixes.Any(p => i.Key.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Key: g.Key, DeletesSubtree: g.Any(DeletesSubtree)))
            .ToList();

        var tempFolder = Path.Combine(Path.GetTempPath(), $"sysmon_regbackup_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
        try
        {
            var sections = new List<string>();
            var failed = new List<string>();
            var exported = 0;

            for (int i = 0; i < keys.Count; i++)
            {
                var unreadable = FindUnreadable(keys[i].Key, keys[i].DeletesSubtree);
                if (unreadable != null)
                {
                    failed.Add($"{unreadable}: cannot be read, so it cannot be backed up");
                    continue;
                }

                var exportFile = Path.Combine(tempFolder, $"{i}.reg");
                var (exitCode, output) = await RunRegAsync(["export", keys[i].Key, exportFile, "/y"]);

                if (exitCode == 0 && File.Exists(exportFile))
                {
                    var lines = await File.ReadAllLinesAsync(exportFile);
                    if (lines.Length == 0 || lines[0] != RegFileHeader)
                    {
                        failed.Add($"{keys[i].Key}: unexpected export format");
                        continue;
                    }
                    sections.AddRange(lines.Skip(1));
                    exported++;
                }
                else if (output.Contains("unable to find", StringComparison.OrdinalIgnoreCase))
                {
                    // The key no longer exists, so fixing the issue cannot change anything.
                }
                else
                {
                    failed.Add($"{keys[i].Key}: {output.Trim()}");
                }
            }

            if (failed.Count > 0)
            {
                return new RegistryBackupResult
                {
                    Success = false,
                    FailedKeys = failed,
                    Message = $"Could not back up {failed.Count} registry key(s); nothing was changed. First: {failed[0]}"
                };
            }

            if (exported == 0)
            {
                return new RegistryBackupResult { Success = true, Message = "None of the selected keys exist any more; nothing to back up." };
            }

            Directory.CreateDirectory(_backupFolder);
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss-fff");
            var backupPath = Path.Combine(_backupFolder, $"registry_backup_{timestamp}.reg");
            var content = new List<string>
            {
                RegFileHeader,
                "",
                $"; SysMonitor registry backup, {DateTime.Now:yyyy-MM-dd HH:mm:ss}: {exported} key(s) that registry cleaning was about to modify.",
                "; Restore from the Registry Cleaner page, or double-click this file to import it with Registry Editor."
            };
            content.AddRange(sections);

            // Same encoding reg.exe and Registry Editor write: UTF-16 LE with a byte-order mark.
            await File.WriteAllLinesAsync(backupPath, content, new System.Text.UnicodeEncoding(bigEndian: false, byteOrderMark: true));

            // Record what was written, so a restore can tell this file apart from one somebody else dropped
            // into the folder. See RestoreRegistryBackupAsync for why that matters.
            await RecordFingerprintAsync(backupPath);

            return new RegistryBackupResult
            {
                Success = true,
                BackupPath = backupPath,
                KeysExported = exported,
                Message = $"Backed up {exported} registry key(s) to {backupPath}"
            };
        }
        finally
        {
            try { Directory.Delete(tempFolder, true); } catch (IOException ex) { _logger.LogDebug(ex, "BackupRegistryAsync failed"); } catch (UnauthorizedAccessException ex) { _logger.LogDebug(ex, "BackupRegistryAsync failed"); }
        }
    }

    /// <summary>
    /// Imports a backup this app wrote.
    /// <para>
    /// The backup folder lives under %LocalAppData%, which every process running as this user can write to,
    /// and a machine-wide import runs reg.exe elevated. Those two facts together would make this method a
    /// way into HKLM: drop a .reg file with a newer timestamp than the real backup, wait for the user to
    /// click "Restore last backup", and their UAC approval - which they are giving to this app - is spent
    /// importing somebody else's keys. Checking that the file begins with the Registry Editor header does
    /// not distinguish the two, because anyone can write that line.
    /// </para>
    /// <para>
    /// So a file is imported only if its bytes match what was recorded when this app wrote that backup, and
    /// the file is held open for reading - denying writers - from the moment it is checked until reg.exe has
    /// finished with it. Without the lock the check and the import are two separate reads of a file that
    /// anyone can rewrite in between, and the UAC prompt is a generous window to do it in.
    /// </para>
    /// </summary>
    public async Task<RegistryRestoreResult> RestoreRegistryBackupAsync(string backupPath)
    {
        if (!File.Exists(backupPath))
            return new RegistryRestoreResult { Message = "Backup file not found." };

        // FileShare.Read lets reg.exe read it too, and keeps every writer out until this handle closes.
        using var locked = new FileStream(backupPath, FileMode.Open, FileAccess.Read, FileShare.Read);

        string content;
        using (var reader = new StreamReader(locked, detectEncodingFromByteOrderMarks: true, leaveOpen: true))
            content = await reader.ReadToEndAsync();

        if (!content.TrimStart('\uFEFF').StartsWith(RegFileHeader, StringComparison.Ordinal))
            return new RegistryRestoreResult { Message = "The file is not a registry backup." };

        locked.Position = 0;
        if (!await MatchesRecordedFingerprintAsync(backupPath, locked))
        {
            return new RegistryRestoreResult
            {
                Message = "This file is not one this app wrote, or it has been changed since. " +
                          "It will not be imported. Registry backups made by this app restore normally."
            };
        }

        var machineWide = content.Contains("[HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ||
                          content.Contains("[HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase);

        if (!machineWide || ElevatedRegistryHelper.IsRunningElevated())
        {
            var (exitCode, output) = await RunRegAsync(["import", backupPath]);
            return exitCode == 0
                ? new RegistryRestoreResult { Success = true, Message = "Registry backup restored." }
                : new RegistryRestoreResult { Message = $"Restore failed: {output.Trim()}" };
        }

        // Machine-wide keys need administrator rights; reg.exe runs elevated after a UAC prompt.
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"import \"{backupPath}\"",
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            });
            if (process == null)
                return new RegistryRestoreResult { Message = "Could not start the restore." };

            await process.WaitForExitAsync();
            return process.ExitCode == 0
                ? new RegistryRestoreResult { Success = true, Message = "Registry backup restored." }
                : new RegistryRestoreResult { Message = $"Restore failed (reg.exe exit code {process.ExitCode})." };
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new RegistryRestoreResult { WasCancelled = true, Message = "Restore cancelled: administrator permission was not granted." };
        }
    }

    /// <summary>Fixes in these categories delete the issue's whole key; all others delete values inside it.</summary>
    private static bool DeletesSubtree(RegistryIssue issue) =>
        issue.Category is RegistryIssueCategory.OrphanedSoftware
            or RegistryIssueCategory.InvalidCOM
            or RegistryIssueCategory.InvalidTypeLib;

    /// <summary>
    /// Returns null when the key's values (and, if <paramref name="includeSubtree"/>, every descendant key) can be
    /// read; otherwise the first path that cannot. A key that does not exist returns null.
    /// </summary>
    internal static string? FindUnreadable(string fullKey, bool includeSubtree)
    {
        RegistryKey? hive = null;
        var subPath = fullKey;
        foreach (var (prefix, candidate) in new (string, RegistryKey)[]
                 {
                     ("HKEY_CURRENT_USER\\", Registry.CurrentUser), ("HKCU\\", Registry.CurrentUser),
                     ("HKEY_LOCAL_MACHINE\\", Registry.LocalMachine), ("HKLM\\", Registry.LocalMachine),
                     ("HKEY_CLASSES_ROOT\\", Registry.ClassesRoot), ("HKCR\\", Registry.ClassesRoot)
                 })
        {
            if (fullKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                hive = candidate;
                subPath = fullKey[prefix.Length..];
                break;
            }
        }
        if (hive == null)
            return fullKey;

        return FindUnreadable(hive, subPath, fullKey, includeSubtree);
    }

    private static string? FindUnreadable(RegistryKey hive, string subPath, string displayPath, bool includeSubtree)
    {
        RegistryKey? key;
        try
        {
            key = hive.OpenSubKey(subPath, writable: false);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return displayPath;
        }

        if (key == null)
            return null;

        using (key)
        {
            try
            {
                foreach (var valueName in key.GetValueNames())
                    _ = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);

                if (!includeSubtree)
                    return null;

                foreach (var child in key.GetSubKeyNames())
                {
                    var unreadable = FindUnreadable(hive, $@"{subPath}\{child}", $@"{displayPath}\{child}", includeSubtree: true);
                    if (unreadable != null)
                        return unreadable;
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return displayPath;
            }
        }

        return null;
    }

    private static async Task<(int ExitCode, string Output)> RunRegAsync(string[] arguments)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("reg.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start reg.exe");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, (await stdout) + (await stderr));
    }
}
