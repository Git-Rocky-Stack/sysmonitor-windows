using Microsoft.Win32;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Cleaners;

/// <summary>
/// Helper class for elevated registry operations.
/// Handles serialization of issues and launching elevated processes.
/// </summary>
public static class ElevatedRegistryHelper
{
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
    /// Launch an elevated process to fix registry issues.
    /// <para>
    /// The two sides talk over a named pipe that only this account can open, never through files. The list
    /// and the result used to go through files in a folder any program running as the user can write to, and
    /// the elevated side wrote its result there: a link planted at that name would have had an administrator
    /// write over a file of the attacker's choosing. A pipe has no path to plant anything at, and the elevated
    /// side connects at identification level, so the side that made the pipe cannot borrow its rights.
    /// </para>
    /// </summary>
    public static async Task<ElevatedCleanResult> RunElevatedCleanAsync(IEnumerable<RegistryIssue> issues)
    {
        try
        {
            var issueList = issues.Where(i => i.IsSelected).ToList();
            var payload = JsonSerializer.SerializeToUtf8Bytes(issueList);

            // The elevated side is handed the fingerprint of the list on its command line, which nothing else
            // can rewrite, and acts only on a list that matches it.
            var fingerprint = Convert.ToHexString(SHA256.HashData(payload));

            // Environment.ProcessPath is the same value as Process.GetCurrentProcess().MainModule.FileName
            // without opening a handle on ourselves that nothing then closes.
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                return new ElevatedCleanResult
                {
                    Success = false,
                    ErrorMessage = "Could not determine executable path"
                };
            }

            var pipeName = $"SysMonitor-registry-{Guid.NewGuid():N}";
            await using var pipe = CreatePipe(pipeName);
            using var exchangeCts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            var exchange = ServeAsync(pipe, payload, exchangeCts.Token);

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--fix-registry {pipeName} {fingerprint}",
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
                // Best effort: the elevated helper has already timed out, and it may have exited by now.
                try { process.Kill(); } catch { }
                return new ElevatedCleanResult
                {
                    Success = false,
                    ErrorMessage = "Elevated process timed out"
                };
            }

            // The helper has exited, so whatever it was going to send has been sent.
            exchangeCts.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await exchange;
            if (result != null)
                return result;

            return process.ExitCode switch
            {
                1223 => new ElevatedCleanResult // ERROR_CANCELLED - user declined UAC
                {
                    Success = false,
                    WasCancelled = true,
                    ErrorMessage = "Elevation was cancelled by user"
                },
                _ => new ElevatedCleanResult
                {
                    Success = false,
                    ErrorMessage = $"The elevated helper exited with code {process.ExitCode} without reporting what it did"
                }
            };
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
    }

    /// <summary>A pipe only this account can open; one connection, so nothing can queue behind the helper.</summary>
    internal static NamedPipeServerStream CreatePipe(string pipeName) =>
        new(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    /// <summary>
    /// The unelevated side of the exchange: hands the list to the helper when it connects and reads back its
    /// result. Null when the helper never connected or sent nothing - the person refused the prompt, say.
    /// </summary>
    internal static async Task<ElevatedCleanResult?> ServeAsync(NamedPipeServerStream pipe, byte[] payload, CancellationToken cancellationToken)
    {
        try
        {
            await pipe.WaitForConnectionAsync(cancellationToken);
            await WriteFrameAsync(pipe, payload, cancellationToken);
            var reply = await ReadFrameAsync(pipe, cancellationToken);
            return reply == null ? null : JsonSerializer.Deserialize<ElevatedCleanResult>(reply);
        }
        catch (Exception ex) when (ex is OperationCanceledException or IOException or JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Execute registry cleaning in elevated mode (called from command line): reads the approved list from the
    /// pipe, cleans what still matches, and sends the result back the same way.
    /// </summary>
    /// <param name="fingerprint">SHA-256 of the list the unelevated side approved, in hex.</param>
    public static async Task<int> ExecuteElevatedClean(string pipeName, string fingerprint)
    {
        try
        {
            // Identification only: whoever made the pipe can learn who connected, and cannot act as them.
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, System.Security.Principal.TokenImpersonationLevel.Identification);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await pipe.ConnectAsync(cts.Token);

            var payload = await ReadFrameAsync(pipe, cts.Token) ?? [];
            var (exitCode, result) = await CleanApprovedAsync(payload, fingerprint);

            await WriteFrameAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(result), CancellationToken.None);
            return exitCode;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    /// <summary>
    /// The elevated side's decision: nothing unless the list is the approved one, and then only what this
    /// process's own scan still reports as broken.
    /// </summary>
    internal static async Task<(int ExitCode, ElevatedCleanResult Result)> CleanApprovedAsync(byte[] payload, string fingerprint)
    {
        var result = new ElevatedCleanResult();

        try
        {
            if (!MatchesFingerprint(payload, fingerprint))
            {
                result.Success = false;
                result.ErrorMessage = "The list of registry fixes changed after it was approved. Nothing was cleaned.";
                return (1, result);
            }

            var issues = JsonSerializer.Deserialize<List<RegistryIssue>>(payload);

            if (issues == null || issues.Count == 0)
            {
                result.Success = true;
                result.Message = "No issues to fix";
                return (0, result);
            }

            // Second gate: clean only what this process's own scan reports as broken. Even a list that
            // matches its fingerprint says what to delete, and this is what decides whether that is true.
            var confirmed = await ConfirmedByOwnScanAsync(issues);
            var refused = issues.Count - confirmed.Count;
            if (confirmed.Count == 0)
            {
                result.Success = false;
                result.ErrorMessage = refused > 0
                    ? $"None of the {refused} entries are still broken according to this machine's own scan. Nothing was cleaned."
                    : "No issues to fix";
                return (refused > 0 ? 1 : 0, result);
            }

            issues = confirmed;

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

            if (refused > 0)
            {
                errors.Add($"{refused} entries were left alone: this machine's own scan no longer reports them as broken.");
            }

            result.Success = fixedCount > 0;
            result.FixedCount = fixedCount;
            result.ErrorCount = errorCount + refused;
            result.Errors = errors;
            result.Message = refused > 0
                ? $"Fixed {fixedCount} of {issues.Count} registry issues; {refused} were left alone"
                : $"Fixed {fixedCount} of {issues.Count} registry issues";

            return (result.Success ? 0 : 1, result);
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = ex.Message;
            return (1, result);
        }
    }

    /// <summary>The largest list either side accepts: far beyond any scan, far short of exhausting memory.</summary>
    private const int MaxFrameBytes = 16 * 1024 * 1024;

    /// <summary>A length, then that many bytes: a pipe has no end-of-message of its own in byte mode.</summary>
    internal static async Task WriteFrameAsync(Stream stream, byte[] body, CancellationToken cancellationToken)
    {
        var length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, body.Length);
        await stream.WriteAsync(length, cancellationToken);
        await stream.WriteAsync(body, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    /// <summary>The next frame, or null when the other side closed without sending one.</summary>
    internal static async Task<byte[]?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
    {
        var length = new byte[4];
        if (await stream.ReadAtLeastAsync(length, 4, throwOnEndOfStream: false, cancellationToken) < 4)
            return null;

        var size = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(length);
        if (size is < 0 or > MaxFrameBytes)
            throw new IOException("The other side sent a frame of an impossible size.");

        var body = new byte[size];
        await stream.ReadExactlyAsync(body, cancellationToken);
        return body;
    }

    /// <summary>Whether a payload is the one whose fingerprint was passed on the command line.</summary>
    internal static bool MatchesFingerprint(byte[] payload, string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            return false;

        byte[] expected;
        try
        {
            expected = Convert.FromHexString(fingerprint.Trim());
        }
        catch (FormatException)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(SHA256.HashData(payload), expected);
    }

    /// <summary>
    /// The entries a fresh scan on this machine agrees are broken. An entry counts only when the same key,
    /// value and kind of problem come back, so the approved list can shrink but never grow into something else.
    /// </summary>
    internal static async Task<List<RegistryIssue>> ConfirmedByOwnScanAsync(
        IEnumerable<RegistryIssue> issues, IRegistryCleaner? cleaner = null)
    {
        var scanned = await (cleaner ?? new RegistryCleaner()).ScanAsync();
        var broken = new HashSet<string>(scanned.Select(Identity), StringComparer.OrdinalIgnoreCase);

        var confirmed = new List<RegistryIssue>();
        foreach (var issue in issues)
        {
            if (!broken.Contains(Identity(issue)))
                continue;

            issue.IsSelected = true;
            confirmed.Add(issue);
        }

        return confirmed;
    }

    private static string Identity(RegistryIssue issue) => $"{(int)issue.Category}\u0000{issue.Key}\u0000{issue.ValueName}";

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
                // Best effort: the value is reported as not removed either way, by the re-scan that follows.
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
