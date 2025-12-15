using Microsoft.Win32;
using System.Diagnostics;
using Windows.Management.Deployment;

namespace SysMonitor.Core.Services.Utilities;

public class InstalledProgramsService : IInstalledProgramsService
{
    // Known system/bloatware package name patterns
    private static readonly string[] SystemAppPatterns =
    {
        "Microsoft.Xbox", "Microsoft.Gaming", "Microsoft.BingWeather", "Microsoft.BingNews",
        "Microsoft.GetHelp", "Microsoft.Getstarted", "Microsoft.MicrosoftOfficeHub",
        "Microsoft.MicrosoftSolitaireCollection", "Microsoft.People", "Microsoft.WindowsFeedbackHub",
        "Microsoft.WindowsMaps", "Microsoft.YourPhone", "Microsoft.ZuneMusic", "Microsoft.ZuneVideo",
        "Clipchamp", "Microsoft.549981C3F5F10", "Microsoft.Todos", "Microsoft.PowerAutomateDesktop",
        "MicrosoftCorporationII.QuickAssist", "Microsoft.BingSearch", "Microsoft.OutlookForWindows"
    };

    // Framework patterns (usually shouldn't be uninstalled)
    private static readonly string[] FrameworkPatterns =
    {
        "Microsoft.NET", "Microsoft.VCLibs", "Microsoft.VCRedist", "Microsoft.WindowsAppRuntime",
        "Microsoft.UI.Xaml", "Microsoft.Services.Store", "Microsoft.DesktopAppInstaller",
        "Microsoft.StorePurchaseApp", "Microsoft.WindowsStore"
    };

    public async Task<List<InstalledProgram>> GetInstalledProgramsAsync()
    {
        var programs = new List<InstalledProgram>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await Task.Run(() =>
        {
            // Get Win32 apps from Registry
            GetWin32Programs(programs, seenNames);

            // Get Store/UWP apps
            GetStoreApps(programs, seenNames);
        });

        return programs
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void GetWin32Programs(List<InstalledProgram> programs, HashSet<string> seenNames)
    {
        // Registry paths for installed programs
        var registryPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (var path in registryPaths)
        {
            try
            {
                // Check HKLM (machine-wide installs)
                using var hklmKey = Registry.LocalMachine.OpenSubKey(path);
                if (hklmKey != null)
                {
                    EnumerateRegistryPrograms(hklmKey, programs, seenNames, $"HKLM\\{path}");
                }
            }
            catch { }

            try
            {
                // Check HKCU (user installs)
                using var hkcuKey = Registry.CurrentUser.OpenSubKey(path);
                if (hkcuKey != null)
                {
                    EnumerateRegistryPrograms(hkcuKey, programs, seenNames, $"HKCU\\{path}");
                }
            }
            catch { }
        }
    }

    private void EnumerateRegistryPrograms(RegistryKey parentKey, List<InstalledProgram> programs,
        HashSet<string> seenNames, string basePath)
    {
        foreach (var subKeyName in parentKey.GetSubKeyNames())
        {
            try
            {
                using var subKey = parentKey.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                var displayName = subKey.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                // Skip if we've already seen this program
                if (seenNames.Contains(displayName)) continue;

                // Check if it's a system component or should be hidden
                var systemComponent = subKey.GetValue("SystemComponent");
                var parentKeyName = subKey.GetValue("ParentKeyName") as string;

                // Skip system components but not everything (some useful apps are marked as system)
                if (systemComponent is int sc && sc == 1)
                {
                    // Allow some "system components" that users might want to see
                    var releaseType = subKey.GetValue("ReleaseType") as string;
                    if (releaseType == "Update" || releaseType == "Hotfix")
                        continue;
                }

                // Skip entries that are just update references
                if (!string.IsNullOrEmpty(parentKeyName)) continue;

                var program = new InstalledProgram
                {
                    Name = displayName,
                    Publisher = subKey.GetValue("Publisher") as string ?? "",
                    Version = subKey.GetValue("DisplayVersion") as string ?? "",
                    InstallLocation = subKey.GetValue("InstallLocation") as string ?? "",
                    UninstallString = subKey.GetValue("UninstallString") as string ?? "",
                    QuietUninstallString = subKey.GetValue("QuietUninstallString") as string ?? "",
                    RegistryKey = $"{basePath}\\{subKeyName}",
                    Type = ProgramType.Win32,
                    Icon = "\uE74C" // Default app icon
                };

                // Parse install date
                var installDateStr = subKey.GetValue("InstallDate") as string;
                if (!string.IsNullOrEmpty(installDateStr) && installDateStr.Length == 8)
                {
                    if (int.TryParse(installDateStr.Substring(0, 4), out var year) &&
                        int.TryParse(installDateStr.Substring(4, 2), out var month) &&
                        int.TryParse(installDateStr.Substring(6, 2), out var day))
                    {
                        try
                        {
                            program.InstallDate = new DateTime(year, month, day);
                        }
                        catch { }
                    }
                }

                // Get estimated size (in KB in registry)
                var sizeValue = subKey.GetValue("EstimatedSize");
                if (sizeValue is int sizeKb)
                {
                    program.EstimatedSizeBytes = sizeKb * 1024L;
                }

                // Detect if it's a framework
                if (IsFramework(displayName))
                {
                    program.Type = ProgramType.Framework;
                    program.Icon = "\uE943"; // Puzzle piece
                }

                seenNames.Add(displayName);
                programs.Add(program);
            }
            catch { }
        }
    }

    private void GetStoreApps(List<InstalledProgram> programs, HashSet<string> seenNames)
    {
        try
        {
            var packageManager = new PackageManager();
            var packages = packageManager.FindPackagesForUser("");

            foreach (var package in packages)
            {
                try
                {
                    // Skip framework packages from the list (but still allow known ones)
                    if (package.IsFramework && !IsKnownSystemApp(package.Id.Name))
                        continue;

                    // Skip resource packages
                    if (package.IsResourcePackage)
                        continue;

                    // Get display name
                    var displayName = package.DisplayName;
                    if (string.IsNullOrWhiteSpace(displayName) ||
                        displayName.StartsWith("ms-resource:"))
                    {
                        displayName = package.Id.Name;
                    }

                    // Skip if already seen
                    if (seenNames.Contains(displayName)) continue;

                    var isSystemApp = IsKnownSystemApp(package.Id.Name);
                    var isFramework = package.IsFramework || IsFramework(package.Id.Name);

                    var program = new InstalledProgram
                    {
                        Name = displayName,
                        Publisher = package.PublisherDisplayName ?? package.Id.Publisher ?? "",
                        Version = $"{package.Id.Version.Major}.{package.Id.Version.Minor}.{package.Id.Version.Build}",
                        InstallLocation = package.InstalledPath ?? "",
                        PackageFullName = package.Id.FullName,
                        Type = isFramework ? ProgramType.Framework :
                               isSystemApp ? ProgramType.SystemApp :
                               ProgramType.StoreApp,
                        IsSystemApp = isSystemApp,
                        InstallDate = package.InstalledDate.DateTime,
                        Icon = isSystemApp ? "\uE770" : // Windows icon for system apps
                               isFramework ? "\uE943" : // Puzzle for frameworks
                               "\uE8F1" // Store icon for store apps
                    };

                    // Try to get package size
                    try
                    {
                        if (!string.IsNullOrEmpty(package.InstalledPath) &&
                            Directory.Exists(package.InstalledPath))
                        {
                            program.EstimatedSizeBytes = GetDirectorySize(package.InstalledPath);
                        }
                    }
                    catch { }

                    seenNames.Add(displayName);
                    programs.Add(program);
                }
                catch { }
            }
        }
        catch { }
    }

    public async Task<UninstallResult> UninstallProgramAsync(InstalledProgram program)
    {
        return await Task.Run(async () =>
        {
            try
            {
                if (program.Type == ProgramType.StoreApp || program.Type == ProgramType.SystemApp)
                {
                    return await UninstallStoreAppAsync(program);
                }
                else
                {
                    return UninstallWin32App(program);
                }
            }
            catch (Exception ex)
            {
                return new UninstallResult
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        });
    }

    private UninstallResult UninstallWin32App(InstalledProgram program)
    {
        var uninstallString = !string.IsNullOrEmpty(program.QuietUninstallString)
            ? program.QuietUninstallString
            : program.UninstallString;

        if (string.IsNullOrEmpty(uninstallString))
        {
            return new UninstallResult
            {
                Success = false,
                Message = "No uninstall command found"
            };
        }

        try
        {
            // Parse the uninstall string
            string fileName;
            string arguments;

            if (uninstallString.StartsWith("\""))
            {
                var endQuote = uninstallString.IndexOf('"', 1);
                fileName = uninstallString.Substring(1, endQuote - 1);
                arguments = uninstallString.Substring(endQuote + 1).Trim();
            }
            else if (uninstallString.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase))
            {
                fileName = "msiexec.exe";
                arguments = uninstallString.Substring(7).Trim();
                // Add quiet flag if not present
                if (!arguments.Contains("/quiet", StringComparison.OrdinalIgnoreCase) &&
                    !arguments.Contains("/qn", StringComparison.OrdinalIgnoreCase))
                {
                    arguments += " /quiet /norestart";
                }
            }
            else
            {
                var spaceIndex = uninstallString.IndexOf(' ');
                if (spaceIndex > 0)
                {
                    fileName = uninstallString.Substring(0, spaceIndex);
                    arguments = uninstallString.Substring(spaceIndex + 1);
                }
                else
                {
                    fileName = uninstallString;
                    arguments = "";
                }
            }

            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                Verb = "runas" // Request elevation
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(60000); // Wait up to 60 seconds

            return new UninstallResult
            {
                Success = process?.ExitCode == 0,
                ExitCode = process?.ExitCode ?? -1,
                Message = process?.ExitCode == 0 ? "Uninstall started" : "Uninstall may have failed"
            };
        }
        catch (Exception ex)
        {
            return new UninstallResult
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    private async Task<UninstallResult> UninstallStoreAppAsync(InstalledProgram program)
    {
        if (string.IsNullOrEmpty(program.PackageFullName))
        {
            return new UninstallResult
            {
                Success = false,
                Message = "Package name not found"
            };
        }

        // Check if this is a protected system app in C:\Windows\SystemApps
        if (!string.IsNullOrEmpty(program.InstallLocation) &&
            program.InstallLocation.Contains(@"\Windows\SystemApps", StringComparison.OrdinalIgnoreCase))
        {
            // These apps require elevated PowerShell with special handling
            return await UninstallProtectedSystemAppAsync(program);
        }

        try
        {
            var packageManager = new PackageManager();
            var operation = packageManager.RemovePackageAsync(program.PackageFullName);

            // Use TaskCompletionSource to properly await the operation
            var tcs = new TaskCompletionSource<UninstallResult>();

            operation.Completed = (asyncInfo, status) =>
            {
                if (status == Windows.Foundation.AsyncStatus.Completed)
                {
                    tcs.SetResult(new UninstallResult
                    {
                        Success = true,
                        Message = "Uninstall completed successfully"
                    });
                }
                else if (status == Windows.Foundation.AsyncStatus.Error)
                {
                    var errorText = asyncInfo.GetResults()?.ErrorText ?? "Unknown error";
                    tcs.SetResult(new UninstallResult
                    {
                        Success = false,
                        Message = $"Uninstall failed: {errorText}"
                    });
                }
                else
                {
                    tcs.SetResult(new UninstallResult
                    {
                        Success = false,
                        Message = "Uninstall was cancelled"
                    });
                }
            };

            // Wait for completion with timeout
            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(60000));
            if (completedTask == tcs.Task)
            {
                return await tcs.Task;
            }
            else
            {
                return new UninstallResult
                {
                    Success = false,
                    Message = "Uninstall timed out"
                };
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Access denied - try PowerShell with elevation
            return await UninstallViaElevatedPowerShellAsync(program);
        }
        catch (Exception ex)
        {
            // Try PowerShell as fallback for other errors
            return await UninstallViaElevatedPowerShellAsync(program, ex.Message);
        }
    }

    private async Task<UninstallResult> UninstallProtectedSystemAppAsync(InstalledProgram program)
    {
        // Extract package name (without version/architecture suffix) for wildcard matching
        var packageName = program.PackageFullName;
        var underscoreIndex = packageName.IndexOf('_');
        if (underscoreIndex > 0)
        {
            packageName = packageName.Substring(0, underscoreIndex);
        }

        // For system apps, we need to:
        // 1. Remove for current user
        // 2. Remove provisioned package to prevent reinstall
        var script = $@"
$ErrorActionPreference = 'Stop'
try {{
    # Remove for current user
    $pkg = Get-AppxPackage -Name '*{packageName}*' -ErrorAction SilentlyContinue
    if ($pkg) {{
        $pkg | Remove-AppxPackage -ErrorAction Stop
        Write-Output 'SUCCESS: Package removed for current user'
    }} else {{
        Write-Output 'WARNING: Package not found for current user'
    }}

    # Also try to remove provisioned package (prevents reinstall for new users)
    $provisioned = Get-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue | Where-Object {{ $_.PackageName -like '*{packageName}*' }}
    if ($provisioned) {{
        $provisioned | Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue
        Write-Output 'SUCCESS: Provisioned package removed'
    }}
}} catch {{
    Write-Output ""ERROR: $($_.Exception.Message)""
    exit 1
}}
";

        return await RunPowerShellScriptAsync(script, "System app removal");
    }

    private async Task<UninstallResult> UninstallViaElevatedPowerShellAsync(InstalledProgram program, string? previousError = null)
    {
        // Extract package name for wildcard matching
        var packageName = program.PackageFullName;
        var underscoreIndex = packageName.IndexOf('_');
        if (underscoreIndex > 0)
        {
            packageName = packageName.Substring(0, underscoreIndex);
        }

        var script = $@"
$ErrorActionPreference = 'Stop'
try {{
    $pkg = Get-AppxPackage -Name '*{packageName}*' -AllUsers -ErrorAction SilentlyContinue
    if ($pkg) {{
        $pkg | Remove-AppxPackage -AllUsers -ErrorAction Stop
        Write-Output 'SUCCESS: Package removed'
    }} else {{
        # Try current user only
        $pkg = Get-AppxPackage -Name '*{packageName}*' -ErrorAction SilentlyContinue
        if ($pkg) {{
            $pkg | Remove-AppxPackage -ErrorAction Stop
            Write-Output 'SUCCESS: Package removed for current user'
        }} else {{
            Write-Output 'ERROR: Package not found'
            exit 1
        }}
    }}
}} catch {{
    Write-Output ""ERROR: $($_.Exception.Message)""
    exit 1
}}
";

        var result = await RunPowerShellScriptAsync(script, "PowerShell removal");

        // Add context about previous error if any
        if (!result.Success && !string.IsNullOrEmpty(previousError))
        {
            result.Message = $"Initial error: {previousError}. {result.Message}";
        }

        return result;
    }

    private async Task<UninstallResult> RunPowerShellScriptAsync(string script, string operationType)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Write script to temp file to avoid command line escaping issues
                var scriptPath = Path.Combine(Path.GetTempPath(), $"sysmon_uninstall_{Guid.NewGuid():N}.ps1");
                File.WriteAllText(scriptPath, script);

                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPath}\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    using var process = Process.Start(psi);
                    if (process == null)
                    {
                        return new UninstallResult
                        {
                            Success = false,
                            Message = "Failed to start PowerShell process"
                        };
                    }

                    process.WaitForExit(60000);

                    if (process.ExitCode == 0)
                    {
                        return new UninstallResult
                        {
                            Success = true,
                            Message = $"{operationType} completed successfully"
                        };
                    }
                    else
                    {
                        return new UninstallResult
                        {
                            Success = false,
                            ExitCode = process.ExitCode,
                            Message = $"{operationType} failed (exit code: {process.ExitCode}). The app may be protected by Windows or require a restart."
                        };
                    }
                }
                finally
                {
                    // Clean up temp script file
                    try { File.Delete(scriptPath); } catch { }
                }
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // User cancelled UAC prompt
                return new UninstallResult
                {
                    Success = false,
                    Message = "Uninstall cancelled - administrator privileges required"
                };
            }
            catch (Exception ex)
            {
                return new UninstallResult
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                };
            }
        });
    }

    public void OpenInstallLocation(InstalledProgram program)
    {
        if (string.IsNullOrEmpty(program.InstallLocation) ||
            !Directory.Exists(program.InstallLocation))
            return;

        try
        {
            Process.Start("explorer.exe", program.InstallLocation);
        }
        catch { }
    }

    private static bool IsKnownSystemApp(string packageName)
    {
        return SystemAppPatterns.Any(p =>
            packageName.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsFramework(string name)
    {
        return FrameworkPatterns.Any(p =>
            name.Contains(p, StringComparison.OrdinalIgnoreCase)) ||
            name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Runtime", StringComparison.OrdinalIgnoreCase);
    }

    private static long GetDirectorySize(string path)
    {
        long size = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try { size += new FileInfo(file).Length; } catch { }
            }
        }
        catch { }
        return size;
    }
}
