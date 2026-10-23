using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Management;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using SysMonitor.Core.Helpers;

namespace SysMonitor.Core.Services.Backup;

/// <summary>A file the backup could not open because another program holds it, and where its copy goes.</summary>
public sealed record LockedFile(string Source, string Destination);

/// <summary>What happened to one locked file: copied from a shadow copy, or why not.</summary>
public sealed record ShadowCopyOutcome(LockedFile File, bool Copied, string? Error);

/// <summary>Takes a shadow copy of a volume: Windows' frozen, readable view of it at one moment.</summary>
internal interface IShadowSnapshots
{
    /// <summary>A snapshot of the volume at <paramref name="volumeRoot"/>; disposing it deletes the shadow copy.</summary>
    IShadowSnapshot Create(string volumeRoot);
}

internal interface IShadowSnapshot : IDisposable
{
    /// <summary>Where a file on the live volume is inside the snapshot.</summary>
    string PathOf(string livePath);
}

/// <summary>
/// Copies files another program holds open - a mail store, a database, an open document - from a shadow copy
/// (Volume Shadow Copy Service), which reads them as they were at one moment without disturbing the program.
/// <para>
/// A shadow copy needs administrator rights, and so does reading from one. When the app is not running as
/// administrator, a helper is: the app starts itself with <c>--vss-copy</c>, Windows asks the person, and the two
/// talk over a named pipe only this account can open. The helper never writes a file. It reads each one from the
/// snapshot and streams the bytes back, and the app writes them into the backup as the person - so an
/// administrator's write can never be pointed somewhere by a planted link. It also reads only what the person's
/// own, unelevated account could read (<see cref="LinkedUserAccess"/>): a file that was merely in use, never one
/// the person was not allowed to open.
/// </para>
/// </summary>
public static class ShadowCopy
{
    /// <summary>The size of the pieces a file travels in over the pipe.</summary>
    private const int ChunkBytes = 1024 * 1024;

    private sealed record Header(int Index, bool Ok, string? Error);

    /// <summary>
    /// Whether an open failed because another program holds the file - a sharing or lock violation - rather than
    /// for want of permission. Windows checks permission first, so a sharing violation also says the account may
    /// read the file.
    /// </summary>
    internal static bool IsInUse(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    /// <summary>
    /// Copies the locked files from shadow copies, asking for administrator rights when the app does not have
    /// them. <paramref name="mayAskForElevation"/> is false for a run nobody is watching, which cannot answer a
    /// prompt; without rights, it then copies nothing and says why.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static async Task<List<ShadowCopyOutcome>> CopyLockedAsync(
        IReadOnlyList<LockedFile> files, bool mayAskForElevation, CancellationToken cancellationToken)
    {
        if (files.Count == 0)
            return [];

        if (IsElevated())
        {
            using var access = LinkedUserAccess.Open();
            return await CopyInProcessAsync(files, new WmiShadowSnapshots(), access.MayRead, cancellationToken);
        }

        if (!mayAskForElevation)
            return Fail(files, "it is in use, and a shadow copy needs administrator rights, which this scheduled run does not have");

        return await CopyThroughHelperAsync(files, cancellationToken);
    }

    private static List<ShadowCopyOutcome> Fail(IEnumerable<LockedFile> files, string why) =>
        files.Select(file => new ShadowCopyOutcome(file, false, $"Not copied: {why}.")).ToList();

    /// <summary>Copies from snapshots in this process, which already has administrator rights.</summary>
    internal static async Task<List<ShadowCopyOutcome>> CopyInProcessAsync(
        IReadOnlyList<LockedFile> files, IShadowSnapshots snapshots, Func<string, bool> userMayRead, CancellationToken cancellationToken)
    {
        var outcomes = new ShadowCopyOutcome?[files.Count];

        await ReadFromSnapshotsAsync(files.Select(f => f.Source).ToList(), snapshots, userMayRead, async (index, content, error) =>
        {
            if (content == null)
            {
                outcomes[index] = new ShadowCopyOutcome(files[index], false, error);
                return;
            }

            try
            {
                await using var output = new FileStream(files[index].Destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await content.CopyToAsync(output, cancellationToken);
                outcomes[index] = new ShadowCopyOutcome(files[index], true, null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                outcomes[index] = new ShadowCopyOutcome(files[index], false, ex.Message);
            }
        }, cancellationToken);

        return outcomes.Select((o, i) => o ?? new ShadowCopyOutcome(files[i], false, "Not copied.")).ToList();
    }

    /// <summary>
    /// Takes one snapshot per volume and hands each file's content to <paramref name="each"/>, or the reason it
    /// has none. Every snapshot is deleted before this returns, whatever happened.
    /// </summary>
    internal static async Task ReadFromSnapshotsAsync(
        IReadOnlyList<string> sources, IShadowSnapshots snapshots, Func<string, bool> userMayRead,
        Func<int, Stream?, string?, Task> each, CancellationToken cancellationToken)
    {
        var byVolume = sources
            .Select((source, index) => (Source: source, Index: index))
            .GroupBy(file => VolumeRootOf(file.Source), StringComparer.OrdinalIgnoreCase);

        foreach (var volume in byVolume)
        {
            IShadowSnapshot? snapshot = null;
            string? snapshotError = null;
            try
            {
                snapshot = snapshots.Create(volume.Key);
            }
            catch (Exception ex)
            {
                snapshotError = $"no shadow copy of {volume.Key} could be made: {ex.Message}";
            }

            try
            {
                foreach (var (source, index) in volume)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (snapshot == null)
                    {
                        await each(index, null, $"Not copied: {snapshotError}");
                        continue;
                    }

                    if (!userMayRead(source))
                    {
                        await each(index, null, "Not copied: this account may not read it, so it is not read as administrator either.");
                        continue;
                    }

                    FileStream content;
                    try
                    {
                        content = new FileStream(snapshot.PathOf(source), FileMode.Open, FileAccess.Read,
                            FileShare.ReadWrite | FileShare.Delete, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        await each(index, null, $"Not copied from the shadow copy: {ex.Message}");
                        continue;
                    }

                    await using (content)
                    {
                        await each(index, content, null);
                    }
                }
            }
            finally
            {
                snapshot?.Dispose();
            }
        }
    }

    /// <summary>The root of the volume a path is on: <c>C:\</c>, or the folder a volume is mounted at.</summary>
    internal static string VolumeRootOf(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            var buffer = new char[1024];
            if (GetVolumePathName(path, buffer, (uint)buffer.Length))
                return new string(buffer, 0, Array.IndexOf(buffer, '\0'));
        }

        return Path.GetPathRoot(path) ?? path;
    }

    // ==================== THROUGH THE ELEVATED HELPER ====================

    [SupportedOSPlatform("windows")]
    private static async Task<List<ShadowCopyOutcome>> CopyThroughHelperAsync(IReadOnlyList<LockedFile> files, CancellationToken cancellationToken)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return Fail(files, "the app could not find itself to ask for administrator rights");

        var payload = JsonSerializer.SerializeToUtf8Bytes(files.Select(f => f.Source).ToArray());
        var fingerprint = ElevatedFingerprint.Of(payload);
        var pipeName = $"SysMonitor-vss-{Guid.NewGuid():N}";

        await using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        Process? helper;
        try
        {
            helper = Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"--vss-copy {pipeName} {fingerprint}",
                UseShellExecute = true,
                Verb = "runas",
            });
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return Fail(files, "it is in use, and Windows did not get the administrator permission a shadow copy needs");
        }

        if (helper == null)
            return Fail(files, "the shadow copy helper did not start");

        using (helper)
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var exited = helper.WaitForExitAsync(connectCts.Token);
            var connected = pipe.WaitForConnectionAsync(connectCts.Token);
            if (await Task.WhenAny(connected, exited) != connected)
            {
                connectCts.Cancel();
                return Fail(files, $"the shadow copy helper stopped before it started (code {helper.ExitCode})");
            }

            await connected;
            await PipeFrames.WriteAsync(pipe, payload, cancellationToken);
            var outcomes = await ReceiveAsync(pipe, files, cancellationToken);
            await helper.WaitForExitAsync(cancellationToken);
            return outcomes;
        }
    }

    /// <summary>
    /// The app's side of the stream: for each file, a header, then - when it was read - its bytes in pieces up to
    /// an empty one, which the app writes to the file's place in the backup. A header with index -1 ends it.
    /// </summary>
    internal static async Task<List<ShadowCopyOutcome>> ReceiveAsync(Stream pipe, IReadOnlyList<LockedFile> files, CancellationToken cancellationToken)
    {
        var outcomes = new ShadowCopyOutcome?[files.Count];

        while (await PipeFrames.ReadAsync(pipe, cancellationToken) is { } frame)
        {
            var header = JsonSerializer.Deserialize<Header>(frame);
            if (header == null || header.Index < 0)
                break;
            if (header.Index >= files.Count)
                throw new IOException("The shadow copy helper named a file it was not asked for.");

            var file = files[header.Index];
            if (!header.Ok)
            {
                outcomes[header.Index] = new ShadowCopyOutcome(file, false, header.Error);
                continue;
            }

            // The pieces are read to the end whatever happens to the write, so the stream stays in step.
            FileStream? output = null;
            string? writeError = null;
            try
            {
                output = new FileStream(file.Destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                writeError = ex.Message;
            }

            await using (output)
            {
                while (true)
                {
                    var chunk = await PipeFrames.ReadAsync(pipe, cancellationToken)
                        ?? throw new IOException("The shadow copy helper stopped in the middle of a file.");
                    if (chunk.Length == 0)
                        break;

                    if (output != null && writeError == null)
                    {
                        try
                        {
                            await output.WriteAsync(chunk, cancellationToken);
                        }
                        catch (IOException ex)
                        {
                            writeError = ex.Message;
                        }
                    }
                }
            }

            outcomes[header.Index] = new ShadowCopyOutcome(file, writeError == null, writeError);
        }

        return outcomes.Select((o, i) => o ?? new ShadowCopyOutcome(files[i], false, "Not copied: the shadow copy helper did not send it.")).ToList();
    }

    /// <summary>
    /// The helper's side: reads the list, refuses it unless it is the one whose fingerprint the app put on the
    /// command line, then streams each file from a snapshot.
    /// </summary>
    internal static async Task ServeAsync(Stream pipe, string fingerprint, IShadowSnapshots snapshots, Func<string, bool> userMayRead, CancellationToken cancellationToken)
    {
        var payload = await PipeFrames.ReadAsync(pipe, cancellationToken) ?? [];
        string[] sources;
        try
        {
            sources = ElevatedFingerprint.Matches(payload, fingerprint)
                ? JsonSerializer.Deserialize<string[]>(payload) ?? []
                : [];
        }
        catch (JsonException)
        {
            sources = [];
        }

        var buffer = new byte[ChunkBytes];
        await ReadFromSnapshotsAsync(sources, snapshots, userMayRead, async (index, content, error) =>
        {
            await PipeFrames.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(new Header(index, content != null, error)), cancellationToken);
            if (content == null)
                return;

            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                await PipeFrames.WriteAsync(pipe, buffer.AsMemory(0, read), cancellationToken);
            await PipeFrames.WriteAsync(pipe, ReadOnlyMemory<byte>.Empty, cancellationToken);
        }, cancellationToken);

        await PipeFrames.WriteAsync(pipe, JsonSerializer.SerializeToUtf8Bytes(new Header(-1, false, null)), cancellationToken);
    }

    /// <summary>The helper process, started with <c>--vss-copy &lt;pipe&gt; &lt;fingerprint&gt;</c> as administrator.</summary>
    [SupportedOSPlatform("windows")]
    public static async Task<int> RunHelperAsync(string pipeName, string fingerprint)
    {
        try
        {
            // Identification only: whoever made the pipe can learn who connected, and cannot act as them.
            await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, TokenImpersonationLevel.Identification);
            using (var connect = new CancellationTokenSource(TimeSpan.FromSeconds(30)))
                await pipe.ConnectAsync(connect.Token);

            using var access = LinkedUserAccess.Open();
            await ServeAsync(pipe, fingerprint, new WmiShadowSnapshots(), access.MayRead, CancellationToken.None);
            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    [SupportedOSPlatform("windows")]
    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumePathNameW")]
    private static extern bool GetVolumePathName(string fileName, [Out] char[] volumePathName, uint bufferLength);
}

/// <summary>
/// Shadow copies through WMI's Win32_ShadowCopy: <c>Create</c> with the ClientAccessible context, which Windows'
/// own "Previous Versions" uses, then read through the copy's device path, and deleted when done.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WmiShadowSnapshots : IShadowSnapshots
{
    public IShadowSnapshot Create(string volumeRoot)
    {
        var root = volumeRoot.EndsWith('\\') ? volumeRoot : volumeRoot + "\\";

        using var shadowClass = new ManagementClass(new ManagementPath(@"\\.\root\cimv2:Win32_ShadowCopy"));
        using var inputs = shadowClass.GetMethodParameters("Create");
        inputs["Volume"] = root;
        inputs["Context"] = "ClientAccessible";

        using var outputs = shadowClass.InvokeMethod("Create", inputs, null);
        var code = Convert.ToUInt32(outputs["ReturnValue"]);
        if (code != 0)
            throw new IOException(Describe(code));

        var id = outputs["ShadowID"] as string;
        if (!Guid.TryParse(id, out var shadowId))
            throw new IOException("Windows made a shadow copy but did not say which one.");

        using var searcher = new ManagementObjectSearcher(@"root\cimv2", $"SELECT * FROM Win32_ShadowCopy WHERE ID='{shadowId:B}'");
        var shadow = searcher.Get().Cast<ManagementObject>().FirstOrDefault()
            ?? throw new IOException("Windows made a shadow copy and then could not find it.");

        return new Snapshot(shadow, (string)shadow["DeviceObject"], root);
    }

    private static string Describe(uint code) => code switch
    {
        1 => "access was denied",
        2 => "Windows rejected the request",
        3 => "the volume was not found",
        4 => "the volume does not support shadow copies",
        5 => "this edition of Windows does not support the kind of shadow copy asked for",
        6 => "there is not enough space for a shadow copy",
        7 => "the volume is busy",
        8 => "the volume already has the most shadow copies it can keep",
        9 => "another shadow copy is being made; try again shortly",
        10 or 11 or 12 => "the shadow copy provider failed",
        _ => $"Windows answered {code}",
    };

    private sealed class Snapshot(ManagementObject shadow, string device, string root) : IShadowSnapshot
    {
        public string PathOf(string livePath) => device + "\\" + livePath[root.Length..];

        public void Dispose()
        {
            try
            {
                shadow.Delete();
            }
            catch (ManagementException)
            {
                // Best effort: a client-accessible copy Windows could not delete is removed with the oldest
                // when the volume's shadow storage fills, and leaving it costs only that space.
            }
            finally
            {
                shadow.Dispose();
            }
        }
    }
}

/// <summary>
/// What the person's own, unelevated account may read. An administrator's Windows sign-in has two halves: the
/// elevated one the helper runs with, and the limited one everything else does. Windows links them, and the
/// limited half answers whether a file's permissions let the person read it - so the helper, which can read
/// anything, copies only what the person could have opened if no program had been holding it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class LinkedUserAccess : IDisposable
{
    private const int TokenLinkedToken = 19;
    private const uint TokenQuery = 0x0008;
    private const uint FileGenericRead = 0x120089;

    /// <summary>The limited half, or null when there is none: UAC off, or the built-in Administrator.</summary>
    private readonly SafeAccessTokenHandle? _limited;

    private LinkedUserAccess(SafeAccessTokenHandle? limited) => _limited = limited;

    public static LinkedUserAccess Open()
    {
        using var self = Process.GetCurrentProcess();
        if (!OpenProcessToken(self.Handle, TokenQuery, out var own))
            return new LinkedUserAccess(null);

        using (own)
        {
            var size = IntPtr.Size;
            var buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetTokenInformation(own, TokenLinkedToken, buffer, size, out _))
                    return new LinkedUserAccess(null);

                return new LinkedUserAccess(new SafeAccessTokenHandle(Marshal.ReadIntPtr(buffer)));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    /// <summary>
    /// Whether the person may read the file. With no limited half, the person is an administrator without UAC
    /// and may read anything this process can.
    /// </summary>
    public bool MayRead(string path)
    {
        if (_limited == null)
            return true;

        try
        {
            var descriptor = new FileInfo(path)
                .GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner | AccessControlSections.Group)
                .GetSecurityDescriptorBinaryForm();

            var mapping = new GenericMapping { Read = 0x120089, Write = 0x120116, Execute = 0x1200A0, All = 0x1F01FF };
            var privileges = new byte[256];
            var privilegesLength = (uint)privileges.Length;

            return AccessCheck(descriptor, _limited, FileGenericRead, ref mapping, privileges, ref privilegesLength, out _, out var allowed)
                   && allowed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return false;
        }
    }

    public void Dispose() => _limited?.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    private struct GenericMapping
    {
        public uint Read;
        public uint Write;
        public uint Execute;
        public uint All;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out SafeAccessTokenHandle tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(SafeAccessTokenHandle token, int informationClass, IntPtr information, int length, out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AccessCheck(byte[] securityDescriptor, SafeAccessTokenHandle clientToken, uint desiredAccess,
        ref GenericMapping genericMapping, byte[] privilegeSet, ref uint privilegeSetLength, out uint grantedAccess, out bool accessStatus);
}
