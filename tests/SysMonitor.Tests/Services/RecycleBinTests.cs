using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Large Files and Duplicate Finder hand files to the Recycle Bin, and the call underneath asks Windows to
/// recycle without confirmation. When Windows cannot recycle a file - on a network or removable drive, on a
/// drive whose Recycle Bin is turned off, or larger than the Recycle Bin holds - that call deletes it
/// permanently instead, and the page used to say it had been moved to the Recycle Bin.
/// <para>
/// These tests pin down the replacement. Every one of those cases leaves the file where it is and says why;
/// links are never followed; and a file is only ever called recycled when it is found in the Recycle Bin
/// afterwards. The machine is a stand-in throughout, so the suite never puts anything in a real Recycle Bin,
/// from which nothing can reliably take it out again.
/// </para>
/// </summary>
public class RecycleBinTests : IDisposable
{
    private readonly TempDirectory _temp = new("recycle-bin");

    public void Dispose() => _temp.Dispose();

    /// <summary>A machine whose drives, Recycle Bin settings and shell behave as each test says.</summary>
    private sealed class FakeMachine : IRecycleBinHost
    {
        private readonly HashSet<string> _recycleBin = new(StringComparer.OrdinalIgnoreCase);

        public DriveType Type { get; set; } = DriveType.Fixed;

        public string? Root { get; set; } = @"C:\";

        public RecycleBinLimits? Limits { get; set; } = new(TurnedOff: false, CapacityBytes: long.MaxValue);

        /// <summary>False for a shell that deletes the file without putting it in the Recycle Bin.</summary>
        public bool Recycles { get; set; } = true;

        public Exception? WillNotMove { get; set; }

        public List<string> HandedOver { get; } = new();

        public (string? Root, DriveType Type) DriveOf(string fullPath) => (Root, Type);

        public RecycleBinLimits? LimitsOf(string root) => Limits;

        public void MoveToRecycleBin(string fullPath)
        {
            if (WillNotMove is not null)
                throw WillNotMove;

            HandedOver.Add(fullPath);
            File.Delete(fullPath);
            if (Recycles)
                _recycleBin.Add(fullPath);
        }

        public bool IsInRecycleBin(string root, string fullPath, DateTime sinceUtc) => _recycleBin.Contains(fullPath);
    }

    // ---------------------------------------------------------------- what goes

    [Fact]
    public void AFileWindowsCanRecycleIsMovedAndFoundInTheRecycleBin()
    {
        var file = _temp.File("video.mp4", "contents");
        var machine = new FakeMachine();

        var result = new RecycleBin(machine).Send(file);

        result.Outcome.Should().Be(RecycleOutcome.Recycled);
        machine.HandedOver.Should().ContainSingle().Which.Should().Be(file);
        File.Exists(file).Should().BeFalse();
    }

    [Fact]
    public void AFileWindowsRemovedWithoutRecyclingIsNeverCalledRecycled()
    {
        var file = _temp.File("video.mp4", "contents");
        var machine = new FakeMachine { Recycles = false };

        var result = new RecycleBin(machine).Send(file);

        result.Outcome.Should().Be(RecycleOutcome.NotInRecycleBin,
            "a case nobody foresaw is reported as it is: gone, and not where it can be restored from");
    }

    // ---------------------------------------------------------------- what is left where it is

    [Theory]
    [InlineData(DriveType.Network, "on a network drive")]
    [InlineData(DriveType.Removable, "on a removable drive")]
    [InlineData(DriveType.CDRom, "on a drive with no Recycle Bin")]
    [InlineData(DriveType.Ram, "on a drive with no Recycle Bin")]
    [InlineData(DriveType.Unknown, "on a drive with no Recycle Bin")]
    [InlineData(DriveType.NoRootDirectory, "on a drive with no Recycle Bin")]
    public void AFileOnADriveWithoutARecycleBinIsLeftWhereItIs(DriveType type, string reason)
    {
        var file = _temp.File("video.mp4", "contents");
        var machine = new FakeMachine { Type = type };

        var result = new RecycleBin(machine).Send(file);

        result.Should().Be(new RecycleResult(RecycleOutcome.Refused, reason));
        machine.HandedOver.Should().BeEmpty("Windows would have deleted it permanently, so it was never asked");
        File.Exists(file).Should().BeTrue();
    }

    [Fact]
    public void AFixedDriveThatIsNotAVolumeOfItsOwnHasNoRecycleBin()
    {
        // A substituted drive letter looks fixed, but Windows keeps no Recycle Bin for it.
        var machine = new FakeMachine { Limits = null };

        new RecycleBin(machine).WhyWindowsWouldNotRecycle(@"S:\video.mp4", 1, out _)
            .Should().Be("on a drive with no Recycle Bin");
    }

    [Fact]
    public void ADriveWhoseRecycleBinIsTurnedOffLeavesTheFileAlone()
    {
        var machine = new FakeMachine { Limits = new RecycleBinLimits(TurnedOff: true, CapacityBytes: 0) };

        new RecycleBin(machine).WhyWindowsWouldNotRecycle(@"C:\video.mp4", 1, out _)
            .Should().Be("on a drive whose Recycle Bin is turned off");
    }

    [Fact]
    public void AFileLargerThanTheRecycleBinHoldsIsLeftWhereItIs()
    {
        var machine = new FakeMachine
        {
            Root = @"D:\",
            Limits = new RecycleBinLimits(TurnedOff: false, CapacityBytes: 1_000),
        };
        var recycleBin = new RecycleBin(machine);

        recycleBin.WhyWindowsWouldNotRecycle(@"D:\video.mp4", 1_001, out _)
            .Should().Be("larger than the Recycle Bin on D: is set to hold");
        recycleBin.WhyWindowsWouldNotRecycle(@"D:\video.mp4", 1_000, out var root)
            .Should().BeNull("a file that fits is one Windows can recycle");
        root.Should().Be(@"D:\", "it is that drive's Recycle Bin the file is looked for in afterwards");
    }

    [Fact]
    public void APathTooLongForTheRecycleBinIsLeftWhereItIs()
    {
        var longPath = @"C:\" + string.Join('\\', Enumerable.Repeat("folder", 40)) + @"\video.mp4";

        new RecycleBin(new FakeMachine()).WhyWindowsWouldNotRecycle(longPath, 1, out _)
            .Should().Be("with a path too long for the Recycle Bin");
    }

    [Fact]
    public void ANetworkPathIsSeenForWhatItIsBeforeWindowsIsAskedAnything()
    {
        // The real machine, not a stand-in: a UNC path is recognised from its shape alone.
        new WindowsRecycleBinHost().DriveOf(@"\\server\share\video.mp4").Should().Be(((string?)null, DriveType.Network));
        new WindowsRecycleBinHost().DriveOf(@"\\?\UNC\server\share\video.mp4").Type.Should().Be(DriveType.Network);
    }

    [Theory]
    [InlineData(@"C:\Users\me\video.mp4", @"C:\")]
    [InlineData(@"d:/media/video.mp4", @"D:\")]
    [InlineData(@"\\?\C:\Users\me\video.mp4", @"C:\")]
    [InlineData(@"\\.\E:\video.mp4", @"E:\")]
    [InlineData(@"\\server\share\video.mp4", null)]
    [InlineData(@"\\?\UNC\server\share\video.mp4", null)]
    [InlineData("/home/me/video.mp4", null)]
    public void TheDriveAPathIsOnIsReadThroughItsPrefixes(string path, string? root) =>
        RecycleBin.DriveRootOf(path).Should().Be(root);

    [Fact]
    public void ALinkIsNeverFollowedOrMoved()
    {
        var original = _temp.File("original.mp4", "the file itself");
        var link = Path.Combine(_temp.Path, "link.mp4");
        FileSystemLinks.CreateFileSymlink(link, original);
        var machine = new FakeMachine();

        var result = new RecycleBin(machine).Send(link);

        result.Should().Be(new RecycleResult(RecycleOutcome.Refused, "linking to another file"));
        machine.HandedOver.Should().BeEmpty();
        File.Exists(link).Should().BeTrue("removing someone's shortcut is not getting rid of a file");
        File.Exists(original).Should().BeTrue();
    }

    [Fact]
    public void AFileAlreadyGoneIsReportedAsMissingAndNothingIsAsked()
    {
        var machine = new FakeMachine();

        var result = new RecycleBin(machine).Send(Path.Combine(_temp.Path, "gone.mp4"));

        result.Outcome.Should().Be(RecycleOutcome.Missing);
        machine.HandedOver.Should().BeEmpty();
    }

    [Fact]
    public void WhenWindowsWillNotMoveAFileItStaysAndTheReasonIsKept()
    {
        var file = _temp.File("in-use.mp4", "contents");
        var inUse = new IOException("The process cannot access the file because it is being used by another process.");
        var machine = new FakeMachine { WillNotMove = inUse };

        var result = new RecycleBin(machine).Send(file);

        result.Outcome.Should().Be(RecycleOutcome.Failed);
        result.Reason.Should().Be("that Windows would not move");
        result.Error.Should().BeSameAs(inUse, "what Windows said goes to the log");
        File.Exists(file).Should().BeTrue();
    }

    // ---------------------------------------------------------------- what the Recycle Bin records

    [Fact]
    public void ARecordFromWindows10OnwardsReadsBackItsPathAndTime()
    {
        var deleted = new DateTime(2026, 9, 26, 6, 30, 0, DateTimeKind.Utc);

        RecycleBinRecords.TryRead(RecordVersion2(@"C:\Users\me\video.mp4", deleted), out var path, out var when)
            .Should().BeTrue();

        path.Should().Be(@"C:\Users\me\video.mp4");
        when.Should().Be(deleted);
    }

    [Fact]
    public void ARecordFromBeforeWindows10ReadsBackItsPathAndTime()
    {
        var deleted = new DateTime(2015, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        RecycleBinRecords.TryRead(RecordVersion1(@"C:\old\report.docx", deleted), out var path, out var when)
            .Should().BeTrue();

        path.Should().Be(@"C:\old\report.docx");
        when.Should().Be(deleted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(23)]
    [InlineData(27)]
    public void ARecordTooShortToHoldAPathIsNotRead(int length)
    {
        var bytes = RecordVersion2(@"C:\video.mp4", DateTime.UtcNow).Take(length).ToArray();

        RecycleBinRecords.TryRead(bytes, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void ARecordOfAnUnknownVersionIsNotRead()
    {
        var bytes = RecordVersion2(@"C:\video.mp4", DateTime.UtcNow);
        BinaryPrimitives.WriteInt64LittleEndian(bytes, 3);

        RecycleBinRecords.TryRead(bytes, out _, out _).Should().BeFalse();
    }

    [Fact]
    public void TheRecycleBinIsSearchedForThisFileDeletedJustNow()
    {
        var bin = Path.Combine(_temp.Path, "$Recycle.Bin", "S-1-5-21-test");
        Directory.CreateDirectory(bin);
        var handedOver = DateTime.UtcNow;

        File.WriteAllBytes(Path.Combine(bin, "$IABC123.mp4"), RecordVersion2(@"C:\Users\me\video.mp4", handedOver));
        File.WriteAllBytes(Path.Combine(bin, "$RABC123.mp4"), [1, 2, 3]);

        RecycleBinRecords.Contains(bin, @"C:\Users\me\video.mp4", handedOver).Should().BeTrue();
        RecycleBinRecords.Contains(bin, @"c:\users\me\VIDEO.mp4", handedOver).Should().BeTrue("Windows paths ignore case");
        RecycleBinRecords.Contains(bin, @"\\?\C:\Users\me\video.mp4", handedOver).Should().BeTrue();
        RecycleBinRecords.Contains(bin, @"C:\Users\me\other.mp4", handedOver).Should().BeFalse();
    }

    [Fact]
    public void AnEarlierDeletionOfTheSamePathIsNotMistakenForThisOne()
    {
        var bin = Path.Combine(_temp.Path, "$Recycle.Bin", "S-1-5-21-test");
        Directory.CreateDirectory(bin);
        var record = Path.Combine(bin, "$IOLD001.mp4");
        File.WriteAllBytes(record, RecordVersion2(@"C:\Users\me\video.mp4", DateTime.UtcNow.AddDays(-3)));
        File.SetLastWriteTimeUtc(record, DateTime.UtcNow.AddDays(-3));

        RecycleBinRecords.Contains(bin, @"C:\Users\me\video.mp4", DateTime.UtcNow).Should().BeFalse(
            "a copy of the same file deleted last week says nothing about whether this one arrived");
    }

    [Fact]
    public void ARecycleBinThatDoesNotExistHoldsNothing() =>
        RecycleBinRecords.Contains(Path.Combine(_temp.Path, "absent"), @"C:\video.mp4", DateTime.UtcNow)
            .Should().BeFalse();

    // ---------------------------------------------------------------- what the page says

    [Fact]
    public void ABatchThatAllWentSaysHowManyAndHowMuch() =>
        RecycleReport.Describe([Recycled(1_000), Recycled(2_000)], Size)
            .Should().Be("Moved 2 files (3000 B) to the Recycle Bin.");

    [Fact]
    public void AFileLeftWhereItIsIsNamedWithItsReason() =>
        RecycleReport.Describe([Recycled(1_000), Recycled(1_000), Recycled(2_200), Left("on a network drive")], Size)
            .Should().Be("Moved 3 files (4200 B) to the Recycle Bin. 1 file on a network drive was left where it is.");

    [Fact]
    public void FilesLeftForDifferentReasonsAreCountedByReason() =>
        RecycleReport.Describe(
                [Left("on a network drive"), Left("on a network drive"), Left("that Windows would not move", RecycleOutcome.Failed)],
                Size)
            .Should().Be("Nothing was moved to the Recycle Bin. 3 files were left where they are: 2 on a network " +
                         "drive, 1 that Windows would not move.");

    [Fact]
    public void FilesThatWereGoneOrLostAreSaidSoPlainly() =>
        RecycleReport.Describe(
                [Recycled(500), new RecycledFile("a", 0, new RecycleResult(RecycleOutcome.Missing, "no longer there")),
                 new RecycledFile("b", 10, new RecycleResult(RecycleOutcome.NotInRecycleBin, "that Windows removed without putting in the Recycle Bin"))],
                Size)
            .Should().Be("Moved 1 file (500 B) to the Recycle Bin. 1 file was no longer there. 1 file is gone but not " +
                         "in the Recycle Bin: treat it as deleted.");

    // ---------------------------------------------------------------- helpers

    private static string Size(long bytes) => $"{bytes} B";

    private static RecycledFile Recycled(long bytes) =>
        new("file", bytes, new RecycleResult(RecycleOutcome.Recycled, "moved to the Recycle Bin"));

    private static RecycledFile Left(string reason, RecycleOutcome outcome = RecycleOutcome.Refused) =>
        new("file", 1, new RecycleResult(outcome, reason));

    /// <summary>A $I record as Windows 10 and later write it.</summary>
    private static byte[] RecordVersion2(string path, DateTime deletedUtc)
    {
        var name = Encoding.Unicode.GetBytes(path + "\0");
        var bytes = new byte[28 + name.Length];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, 2);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), 12345);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16), deletedUtc.ToFileTimeUtc());
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), path.Length + 1);
        name.CopyTo(bytes, 28);
        return bytes;
    }

    /// <summary>A $I record as Windows Vista to 8.1 wrote it: the path in a fixed 520 bytes.</summary>
    private static byte[] RecordVersion1(string path, DateTime deletedUtc)
    {
        var bytes = new byte[24 + 520];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(8), 12345);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(16), deletedUtc.ToFileTimeUtc());
        Encoding.Unicode.GetBytes(path).CopyTo(bytes, 24);
        return bytes;
    }
}
