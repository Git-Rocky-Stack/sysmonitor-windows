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
                    return await UninstallWin32AppAsync(program);
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

    private async Task<UninstallResult> UninstallWin32AppAsync(InstalledProgram program)
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

        var command = ParseUninstallCommand(uninstallString);
        if (string.IsNullOrEmpty(command.FileName))
        {
            return new UninstallResult
            {
                Success = false,
                Message = $"The uninstall command recorded for \"{program.Name}\" cannot be read: {uninstallString}"
            };
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command.FileName,
                Arguments = command.Arguments,
                UseShellExecute = true,
                Verb = "runas" // Request elevation
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return new UninstallResult
                {
                    Success = false,
                    Message = $"Could not start the uninstaller for \"{program.Name}\"."
                };
            }

            return await WaitForUninstallerAsync(process, program.Name, UninstallTimeout);
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new UninstallResult
            {
                Success = false,
                Message = $"Uninstalling \"{program.Name}\" needs administrator approval, and the prompt was declined."
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

    /// <summary>How long an uninstaller is given before the app stops waiting for it.</summary>
    private static readonly TimeSpan UninstallTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Waits for an uninstaller and turns what it did into a result. One that outlives the wait is left
    /// running and reported as running: it has no exit code yet, and asking for one would throw.
    /// </summary>
    internal static async Task<UninstallResult> WaitForUninstallerAsync(Process process, string programName, TimeSpan timeout)
    {
        using var expiry = new CancellationTokenSource(timeout);
        try
        {
            await process.WaitForExitAsync(expiry.Token);
        }
        catch (OperationCanceledException)
        {
            return new UninstallResult
            {
                Success = false,
                Message = $"The uninstaller for \"{programName}\" is still running after {Describe(timeout)}. It was left alone; check Settings > Apps for the result.",
            };
        }

        return DescribeExitCode(process.ExitCode, programName);
    }

    private static string Describe(TimeSpan timeout) => timeout.TotalMinutes >= 1
        ? $"{timeout.TotalMinutes:0} minute{(timeout.TotalMinutes >= 2 ? "s" : string.Empty)}"
        : $"{timeout.TotalSeconds:0} seconds";

    /// <summary>An uninstall command split into what to run and what to pass it.</summary>
    internal readonly record struct UninstallCommand(string FileName, string Arguments);

    /// <summary>
    /// Splits an UninstallString from the registry into a program and its arguments. The program may be
    /// quoted, may be an unquoted path with spaces, and on most machines is "MsiExec.exe /X{...}" - which
    /// used to be cut seven characters in, leaving msiexec with ".exe /X{...}": it then showed its usage
    /// dialog and never uninstalled anything.
    /// </summary>
    internal static UninstallCommand ParseUninstallCommand(string uninstallString)
    {
        var command = (uninstallString ?? string.Empty).Trim();
        if (command.Length == 0)
        {
            return new UninstallCommand(string.Empty, string.Empty);
        }

        string fileName;
        string arguments;

        if (command.StartsWith('"'))
        {
            var endQuote = command.IndexOf('"', 1);
            if (endQuote < 0)
            {
                return new UninstallCommand(string.Empty, string.Empty);
            }

            fileName = command[1..endQuote];
            arguments = command[(endQuote + 1)..].Trim();
        }
        else
        {
            var end = EndOfUnquotedProgram(command);
            fileName = command[..end];
            arguments = command[end..].Trim();
        }

        if (fileName.Length == 0)
        {
            return new UninstallCommand(string.Empty, string.Empty);
        }

        if (Path.GetFileNameWithoutExtension(fileName).Equals("msiexec", StringComparison.OrdinalIgnoreCase))
        {
            arguments = EnsureWindowsInstallerIsQuiet(arguments);
        }

        return new UninstallCommand(fileName, arguments);
    }

    /// <summary>
    /// Where the program ends in an unquoted command: after the first executable extension that a space or
    /// the end of the string follows, so a folder called "weird.executables" is not mistaken for it.
    /// </summary>
    private static int EndOfUnquotedProgram(string command)
    {
        string[] extensions = [".exe", ".msi", ".bat", ".cmd", ".com"];

        var best = -1;
        foreach (var extension in extensions)
        {
            var index = 0;
            while ((index = command.IndexOf(extension, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                var end = index + extension.Length;
                if (end == command.Length || char.IsWhiteSpace(command[end]))
                {
                    if (best < 0 || end < best)
                        best = end;
                    break;
                }

                index = end;
            }
        }

        if (best > 0)
            return best;

        // Nothing that looks like a program name: take the first word, as Windows would.
        var space = command.IndexOf(' ');
        return space > 0 ? space : command.Length;
    }

    /// <summary>Adds the switches that keep msiexec from asking, unless the command already says how to behave.</summary>
    internal static string EnsureWindowsInstallerIsQuiet(string arguments)
    {
        string[] display = ["/quiet", "/qn", "/qb", "/qr", "/qf", "/passive"];
        if (display.Any(switchName => arguments.Contains(switchName, StringComparison.OrdinalIgnoreCase)))
        {
            return arguments;
        }

        return string.IsNullOrEmpty(arguments) ? "/quiet /norestart" : $"{arguments} /quiet /norestart";
    }

    /// <summary>
    /// What an uninstaller's exit code means. A reboot code is a success that needs a restart, and a product
    /// that is no longer installed is not a failure to report as one.
    /// </summary>
    internal static UninstallResult DescribeExitCode(int exitCode, string programName) => exitCode switch
    {
        0 => new UninstallResult { Success = true, ExitCode = 0, Message = $"\"{programName}\" was uninstalled." },
        3010 or 1641 => new UninstallResult
        {
            Success = true,
            ExitCode = exitCode,
            Message = $"\"{programName}\" was uninstalled. Restart Windows to finish.",
        },
        1605 or 1614 => new UninstallResult
        {
            Success = true,
            ExitCode = exitCode,
            Message = $"\"{programName}\" was not installed any more; its entry was left over.",
        },
        1602 => new UninstallResult
        {
            Success = false,
            ExitCode = exitCode,
            Message = $"Uninstalling \"{programName}\" was cancelled.",
        },
        1603 => new UninstallResult
        {
            Success = false,
            ExitCode = exitCode,
            Message = $"The uninstaller for \"{programName}\" failed (1603). It often means the program is in use or needs a restart first.",
        },
        1618 => new UninstallResult
        {
            Success = false,
            ExitCode = exitCode,
            Message = "Another installation is already running. Wait for it to finish and try again.",
        },
        _ => new UninstallResult
        {
            Success = false,
            ExitCode = exitCode,
            Message = $"The uninstaller for \"{programName}\" reported failure (exit code {exitCode}).",
        },
    };

    /// <summary>
    /// Parses an unquoted uninstall string that may contain spaces in the path.
    /// Example: "C:\Program Files\WinRAR\unins000.exe /SILENT" -> ("C:\Program Files\WinRAR\unins000.exe", "/SILENT")
    /// </summary>
    private static (string fileName, string arguments) ParseUnquotedUninstallString(string uninstallString)
    {
        // Common executable extensions to look for
        var exeExtensions = new[] { ".exe", ".msi", ".bat", ".cmd" };

        foreach (var ext in exeExtensions)
        {
            var extIndex = uninstallString.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
            if (extIndex > 0)
            {
                var endOfExe = extIndex + ext.Length;
                var fileName = uninstallString.Substring(0, endOfExe);
                var arguments = endOfExe < uninstallString.Length
                    ? uninstallString.Substring(endOfExe).Trim()
                    : "";

                // Verify the file exists (if it looks like an absolute path)
                if (fileName.Length > 2 && fileName[1] == ':')
                {
                    if (File.Exists(fileName))
                    {
                        return (fileName, arguments);
                    }
                }
                else
                {
                    // Relative or just executable name
                    return (fileName, arguments);
                }
            }
        }

        // Fallback: split at first space (original behavior)
        var spaceIndex = uninstallString.IndexOf(' ');
        if (spaceIndex > 0)
        {
            return (uninstallString.Substring(0, spaceIndex), uninstallString.Substring(spaceIndex + 1));
        }

        return (uninstallString, "");
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
        if (!TryGetPackageName(program, out var packageName, out var rejected))
        {
            return rejected;
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
        if (!TryGetPackageName(program, out var packageName, out var rejected))
        {
            return rejected;
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

    /// <summary>
    /// The package name to match on, once it is certain it is a name: it is put straight into PowerShell
    /// source, and an Appx name that carried a quote could otherwise add commands of its own.
    /// </summary>
    internal static bool TryGetPackageName(InstalledProgram program, out string packageName, out UninstallResult rejected)
    {
        packageName = program.PackageFullName ?? string.Empty;
        var underscoreIndex = packageName.IndexOf('_');
        if (underscoreIndex > 0)
        {
            packageName = packageName.Substring(0, underscoreIndex);
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(packageName, @"^[A-Za-z0-9][A-Za-z0-9.-]{0,127}$"))
        {
            rejected = new UninstallResult { Success = true };
            return true;
        }

        rejected = new UninstallResult
        {
            Success = false,
            Message = $"\"{program.Name}\" has a package name this uninstaller will not pass to PowerShell. Remove it from Settings > Apps instead."
        };
        return false;
    }

    /// <summary>
    /// The command line that runs a script as administrator without leaving it anywhere to be rewritten:
    /// the script itself is encoded into the arguments, which are fixed when the process starts.
    /// </summary>
    internal static string BuildElevatedPowerShellArguments(string script) =>
        "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
        Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));

    /// <summary>
    /// Runs a script as administrator. The script travels on the command line, encoded, rather than in a
    /// file: a file in the temp folder can be rewritten by any process running as this user while the UAC
    /// prompt is open, and the replacement would then run as administrator.
    /// </summary>
    private async Task<UninstallResult> RunPowerShellScriptAsync(string script, string operationType)
    {
        return await Task.Run(() =>
        {
            try
            {
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = BuildElevatedPowerShellArguments(script),
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

                    if (!process.WaitForExit(60000))
                    {
                        return new UninstallResult
                        {
                            Success = false,
                            Message = $"{operationType} is still running after a minute. It was left alone; check Settings > Apps for the result."
                        };
                    }

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
