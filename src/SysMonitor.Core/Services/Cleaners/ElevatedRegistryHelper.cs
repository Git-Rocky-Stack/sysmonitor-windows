using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Cleaners;

/// <summary>
/// Helper class for elevated registry operations.
/// Handles serialization of issues and launching elevated processes.
/// </summary>
public static class ElevatedRegistryHelper
{
    private static readonly string TempFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SysMonitor", "Temp");

    /// <summary>
    /// Check if any issues require elevation (HKLM or HKCR keys)
    /// </summary>
    public static bool RequiresElevation(IEnumerable<RegistryIssue> issues)
    {
        return issues.Any(i => i.IsSelected &&
            (i.Key.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ||
             i.Key.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ||
             i.Key.StartsWith("HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase) ||
             i.Key.StartsWith("HKCR", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Check if the current process is running elevated
    /// </summary>
    public static bool IsRunningElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// Launch an elevated process to fix registry issues
    /// </summary>
    public static async Task<ElevatedCleanResult> RunElevatedCleanAsync(IEnumerable<RegistryIssue> issues)
    {
        Directory.CreateDirectory(TempFolder);

        var inputFile = Path.Combine(TempFolder, $"reg_issues_{Guid.NewGuid():N}.json");
        var outputFile = Path.Combine(TempFolder, $"reg_results_{Guid.NewGuid():N}.json");

        try
        {
            // Serialize issues to temp file
            var issueList = issues.Where(i => i.IsSelected).ToList();
            var json = JsonSerializer.Serialize(issueList, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(inputFile, json);

            // Get the current executable path
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                return new ElevatedCleanResult
                {
                    Success = false,
                    ErrorMessage = "Could not determine executable path"
                };
            }

            // Launch elevated process
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--fix-registry \"{inputFile}\" \"{outputFile}\"",
                UseShellExecute = true,
                Verb = "runas", // This triggers UAC
                CreateNoWindow = false
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new ElevatedCleanResult
                {
                    Success = false,
                    ErrorMessage = "Failed to start elevated process"
                };
            }

            // Wait for the elevated process to complete (with timeout)
            var completed = await Task.Run(() => process.WaitForExit(120000)); // 2 minute timeout

            if (!completed)
            {
                try { process.Kill(); } catch { }
                return new ElevatedCleanResult
                {
                    Success = false,
                    ErrorMessage = "Elevated process timed out"
                };
            }

            // Read results
            if (File.Exists(outputFile))
            {
                var resultJson = await File.ReadAllTextAsync(outputFile);
                var result = JsonSerializer.Deserialize<ElevatedCleanResult>(resultJson);
                return result ?? new ElevatedCleanResult { Success = false, ErrorMessage = "Failed to parse results" };
            }
            else
            {
                // Check exit code
                if (process.ExitCode == 0)
                {
                    return new ElevatedCleanResult
                    {
                        Success = true,
                        FixedCount = issueList.Count,
                        Message = "Registry cleaning completed"
                    };
                }
                else if (process.ExitCode == 1223) // ERROR_CANCELLED - user declined UAC
                {
                    return new ElevatedCleanResult
                    {
                        Success = false,
                        WasCancelled = true,
                        ErrorMessage = "Elevation was cancelled by user"
                    };
                }
                else
                {
                    return new ElevatedCleanResult
                    {
                        Success = false,
                        ErrorMessage = $"Elevated process exited with code {process.ExitCode}"
                    };
                }
            }
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // User cancelled UAC prompt
            return new ElevatedCleanResult
            {
                Success = false,
                WasCancelled = true,
                ErrorMessage = "Elevation was cancelled by user"
            };
        }
        catch (Exception ex)
        {
            return new ElevatedCleanResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            // Cleanup temp files
            try { if (File.Exists(inputFile)) File.Delete(inputFile); } catch { }
            try { if (File.Exists(outputFile)) File.Delete(outputFile); } catch { }
        }
    }

    /// <summary>
    /// Execute registry cleaning in elevated mode (called from command line)
    /// </summary>
    public static async Task<int> ExecuteElevatedClean(string inputFile, string outputFile)
    {
        var result = new ElevatedCleanResult();

        try
        {
            if (!File.Exists(inputFile))
            {
                result.Success = false;
                result.ErrorMessage = "Input file not found";
                await WriteResultAsync(outputFile, result);
                return 1;
            }

            var json = await File.ReadAllTextAsync(inputFile);
            var issues = JsonSerializer.Deserialize<List<RegistryIssue>>(json);

            if (issues == null || issues.Count == 0)
            {
                result.Success = true;
                result.Message = "No issues to fix";
                await WriteResultAsync(outputFile, result);
                return 0;
            }

            // Mark all as selected (they were filtered before serialization)
            foreach (var issue in issues)
            {
                issue.IsSelected = true;
            }

            // Perform the actual cleaning
            int fixedCount = 0;
            int errorCount = 0;
            var errors = new List<string>();

            foreach (var issue in issues)
            {
                try
                {
                    var success = CleanRegistryIssue(issue);
                    if (success)
                    {
                        fixedCount++;
                    }
                    else
                    {
                        errorCount++;
                        errors.Add($"{issue.Key}: Failed to clean");
                    }
                }
                catch (Exception ex)
                {
                    errorCount++;
                    errors.Add($"{issue.Key}: {ex.Message}");
                }
            }

            result.Success = fixedCount > 0;
            result.FixedCount = fixedCount;
            result.ErrorCount = errorCount;
            result.Errors = errors;
            result.Message = $"Fixed {fixedCount} of {issues.Count} registry issues";

            await WriteResultAsync(outputFile, result);
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            await WriteResultAsync(outputFile, result);
            return 1;
        }
    }

    private static bool CleanRegistryIssue(RegistryIssue issue)
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

        if (root == null) return false;

        // Delete subkey or value based on category
        if (issue.Category == RegistryIssueCategory.OrphanedSoftware ||
            issue.Category == RegistryIssueCategory.InvalidCOM ||
            issue.Category == RegistryIssueCategory.InvalidTypeLib)
        {
            // Delete the entire subkey
            var lastBackslash = keyPath.LastIndexOf('\\');
            if (lastBackslash <= 0) return false;

            var parentPath = keyPath.Substring(0, lastBackslash);
            var subKeyName = keyPath.Substring(lastBackslash + 1);

            using var parentKey = root.OpenSubKey(parentPath, writable: true);
            if (parentKey == null) return false;

            var existsBefore = parentKey.GetSubKeyNames().Contains(subKeyName, StringComparer.OrdinalIgnoreCase);
            if (!existsBefore) return true; // Already gone

            parentKey.DeleteSubKeyTree(subKeyName, throwOnMissingSubKey: false);

            var existsAfter = parentKey.GetSubKeyNames().Contains(subKeyName, StringComparer.OrdinalIgnoreCase);
            return !existsAfter;
        }
        else if (issue.ValueName == "(all values)")
        {
            // Clear all values in the key
            using var key = root.OpenSubKey(keyPath, writable: true);
            if (key == null) return false;

            foreach (var valueName in key.GetValueNames())
            {
                try { key.DeleteValue(valueName, throwOnMissingValue: false); } catch { }
            }
            return true;
        }
        else
        {
            // Delete the specific value
            using var key = root.OpenSubKey(keyPath, writable: true);
            if (key == null) return false;

            var existsBefore = key.GetValueNames().Contains(issue.ValueName, StringComparer.OrdinalIgnoreCase);
            if (!existsBefore) return true; // Already gone

            key.DeleteValue(issue.ValueName, throwOnMissingValue: false);

            var existsAfter = key.GetValueNames().Contains(issue.ValueName, StringComparer.OrdinalIgnoreCase);
            return !existsAfter;
        }
    }

    private static async Task WriteResultAsync(string outputFile, ElevatedCleanResult result)
    {
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(outputFile, json);
    }
}

/// <summary>
/// Result of elevated registry cleaning operation
/// </summary>
public class ElevatedCleanResult
{
    public bool Success { get; set; }
    public bool WasCancelled { get; set; }
    public int FixedCount { get; set; }
    public int ErrorCount { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> Errors { get; set; } = new();
}
