using System.IO.Pipes;
using System.Text.Json;
using FluentAssertions;
using SysMonitor.Core.Helpers;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Files another program holds open are read from a shadow copy. The helper that reads them runs as
/// administrator, so it reads only the list the app vouched for, only what the person could have read, and never
/// writes a file: the bytes come back over the pipe and the app writes them.
/// </summary>
public class ShadowCopyTests : IDisposable
{
    private readonly TempDirectory _root = new("shadow");

    public void Dispose() => _root.Dispose();

    /// <summary>A "snapshot" that is a copy of the volume taken when it is made, as a shadow copy is.</summary>
    private sealed class FrozenSnapshots(string frozenRoot) : IShadowSnapshots
    {
        public List<string> Created { get; } = [];
        public int Deleted { get; private set; }
        public bool Fail { get; init; }

        public IShadowSnapshot Create(string volumeRoot)
        {
            if (Fail) throw new IOException("the volume is busy");
            Created.Add(volumeRoot);
            return new Snapshot(this, volumeRoot, frozenRoot);
        }

        private sealed class Snapshot(FrozenSnapshots owner, string root, string frozen) : IShadowSnapshot
        {
            public string PathOf(string livePath) => Path.Combine(frozen, livePath[root.Length..]);
            public void Dispose() => owner.Deleted++;
        }
    }

    private (string Live, string Frozen) Volume(params (string Name, string Content)[] files)
    {
        foreach (var (name, content) in files)
            _root.File(Path.Combine("frozen", name), content);
        return (Path.GetPathRoot(_root.Path)!, Path.Combine(_root.Path, "frozen"));
    }

    /// <summary>The live path whose place in the "snapshot" is <c>frozen/name</c>.</summary>
    private static string LivePath(string liveRoot, string frozen, string name) =>
        Path.Combine(liveRoot, Path.GetRelativePath(liveRoot, Path.Combine(frozen, name)));

    [Fact]
    public async Task InProcessEachFileIsCopiedFromTheSnapshotAndTheSnapshotIsDeleted()
    {
        var (live, frozen) = Volume(("mail.pst", "as it was"));
        var snapshots = new FrozenSnapshots(live);
        var destination = Path.Combine(_root.Path, "copy.pst");

        var outcomes = await ShadowCopy.CopyInProcessAsync(
            [new LockedFile(LivePath(live, frozen, "mail.pst"), destination)], snapshots, _ => true, CancellationToken.None);

        outcomes.Should().ContainSingle(o => o.Copied);
        File.ReadAllText(destination).Should().Be("as it was");
        snapshots.Created.Should().ContainSingle();
        snapshots.Deleted.Should().Be(1);
    }

    [Fact]
    public async Task AFileThePersonMayNotReadIsNotReadAsAdministrator()
    {
        var (live, frozen) = Volume(("secret.txt", "not yours"));
        var destination = Path.Combine(_root.Path, "copy.txt");

        var outcomes = await ShadowCopy.CopyInProcessAsync(
            [new LockedFile(LivePath(live, frozen, "secret.txt"), destination)], new FrozenSnapshots(live), _ => false, CancellationToken.None);

        outcomes.Single().Copied.Should().BeFalse();
        outcomes.Single().Error.Should().Contain("may not read it");
        File.Exists(destination).Should().BeFalse();
    }

    [Fact]
    public async Task ASnapshotThatCannotBeMadeIsReportedForEveryFileOnItsVolume()
    {
        var (live, frozen) = Volume(("a.db", "a"), ("b.db", "b"));
        var files = new[] { "a.db", "b.db" }
            .Select(name => new LockedFile(LivePath(live, frozen, name), Path.Combine(_root.Path, name))).ToList();

        var outcomes = await ShadowCopy.CopyInProcessAsync(files, new FrozenSnapshots(live) { Fail = true }, _ => true, CancellationToken.None);

        outcomes.Should().HaveCount(2).And.OnlyContain(o => !o.Copied && o.Error!.Contains("the volume is busy"));
    }

    private static async Task<List<ShadowCopyOutcome>> OverAPipe(IReadOnlyList<LockedFile> files, byte[] sentList, string fingerprint,
        IShadowSnapshots snapshots, Func<string, bool> userMayRead)
    {
        var name = $"SysMonitor-test-{Guid.NewGuid():N}";
        await using var server = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        var helper = Task.Run(async () =>
        {
            await client.ConnectAsync();
            await ShadowCopy.ServeAsync(client, fingerprint, snapshots, userMayRead, CancellationToken.None);
        });

        await server.WaitForConnectionAsync();
        await PipeFrames.WriteAsync(server, sentList, CancellationToken.None);
        var outcomes = await ShadowCopy.ReceiveAsync(server, files, CancellationToken.None);
        await helper;
        return outcomes;
    }

    [Fact]
    public async Task ThroughTheHelperTheBytesComeBackAndTheAppWritesThem()
    {
        var big = new string('x', 3 * 1024 * 1024 + 17); // more than one piece
        var (live, frozen) = Volume(("big.bin", big), ("small.txt", "small"));
        var files = new[] { "big.bin", "small.txt" }
            .Select(name => new LockedFile(LivePath(live, frozen, name), Path.Combine(_root.Path, "out-" + name))).ToList();
        var list = JsonSerializer.SerializeToUtf8Bytes(files.Select(f => f.Source).ToArray());

        var outcomes = await OverAPipe(files, list, ElevatedFingerprint.Of(list), new FrozenSnapshots(live), _ => true);

        outcomes.Should().OnlyContain(o => o.Copied);
        File.ReadAllText(files[0].Destination).Should().Be(big);
        File.ReadAllText(files[1].Destination).Should().Be("small");
    }

    [Fact]
    public async Task TheHelperReadsNothingFromAListTheAppDidNotVouchFor()
    {
        var (live, frozen) = Volume(("a.txt", "a"));
        var files = new[] { new LockedFile(LivePath(live, frozen, "a.txt"), Path.Combine(_root.Path, "out-a.txt")) };
        var vouched = JsonSerializer.SerializeToUtf8Bytes(new[] { "something else" });
        var sent = JsonSerializer.SerializeToUtf8Bytes(files.Select(f => f.Source).ToArray());
        var snapshots = new FrozenSnapshots(live);

        var outcomes = await OverAPipe(files, sent, ElevatedFingerprint.Of(vouched), snapshots, _ => true);

        outcomes.Should().OnlyContain(o => !o.Copied);
        snapshots.Created.Should().BeEmpty();
        File.Exists(files[0].Destination).Should().BeFalse();
    }

    [Fact]
    public void ASharingViolationIsInUseAndADenialIsNot()
    {
        ShadowCopy.IsInUse(new IOException("in use", unchecked((int)0x80070020))).Should().BeTrue();
        ShadowCopy.IsInUse(new IOException("locked", unchecked((int)0x80070021))).Should().BeTrue();
        ShadowCopy.IsInUse(new IOException("denied", unchecked((int)0x80070005))).Should().BeFalse();
    }
}
