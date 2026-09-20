using FluentAssertions;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Whatever this finder calls a duplicate can be deleted, so it has to be right: the whole file has to match,
/// and one file reached by two paths is not two files. Nothing here deletes anything outright.
/// </summary>
public class DuplicateFinderTests : IDisposable
{
    private readonly TempDirectory _temp = new("duplicates");
    private readonly DuplicateFinder _finder = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task FilesWithTheSameContentsAreFound()
    {
        _temp.File(@"a\one.txt", "the same contents");
        _temp.File(@"b\two.txt", "the same contents");
        _temp.File(@"b\other.txt", "something else entirely");

        var groups = await _finder.ScanAsync(_temp.Path);

        groups.Should().ContainSingle();
        groups[0].Files.Select(f => f.FileName).Should().BeEquivalentTo(["one.txt", "two.txt"]);
        groups[0].Files.Count(f => f.IsOriginal).Should().Be(1, "one copy is kept");
    }

    [Fact]
    public async Task FilesThatOnlyDifferInTheMiddleAreNotDuplicates()
    {
        // Over ten megabytes, this finder used to judge by the first and last megabyte plus the length:
        // two recordings from one camera, or two disk images, matched and were offered up for deletion.
        var size = 12 * 1024 * 1024;
        var first = new byte[size];
        var second = new byte[size];
        Random.Shared.NextBytes(first);
        first.CopyTo(second, 0);
        second[size / 2] ^= 0xFF;

        await File.WriteAllBytesAsync(Path.Combine(_temp.Path, "first.bin"), first);
        await File.WriteAllBytesAsync(Path.Combine(_temp.Path, "second.bin"), second);

        var groups = await _finder.ScanAsync(_temp.Path);

        groups.Should().BeEmpty("one byte in the middle is still a difference");
    }

    [Fact]
    public async Task LargeFilesThatDoMatchAreStillFound()
    {
        var contents = new byte[12 * 1024 * 1024];
        Random.Shared.NextBytes(contents);
        await File.WriteAllBytesAsync(Path.Combine(_temp.Path, "one.bin"), contents);
        await File.WriteAllBytesAsync(Path.Combine(_temp.Path, "two.bin"), contents);

        var groups = await _finder.ScanAsync(_temp.Path);

        groups.Should().ContainSingle();
        groups[0].Files.Should().HaveCount(2);
    }

    [Fact]
    public async Task OneFileReachedThroughAJunctionIsNotADuplicateOfItself()
    {
        // The case from the review: with data-alias -> data, the scan reported the one photo twice, and
        // deleting "the duplicate" deleted the only copy.
        var data = Path.Combine(_temp.Path, "data");
        Directory.CreateDirectory(data);
        var photo = Path.Combine(data, "photo.bin");
        await File.WriteAllTextAsync(photo, "the only copy there is");
        FileSystemLinks.CreateJunction(Path.Combine(_temp.Path, "data-alias"), data);

        var groups = await _finder.ScanAsync(_temp.Path);

        groups.Should().BeEmpty("the junction leads to the same file, which is not a copy of itself");
        File.Exists(photo).Should().BeTrue();
    }

    [Fact]
    public async Task TwoNamesForOneFileAreNotDuplicates()
    {
        var original = Path.Combine(_temp.Path, "original.txt");
        await File.WriteAllTextAsync(original, "one file, two names");
        FileSystemLinks.CreateHardLink(Path.Combine(_temp.Path, "hard-link.txt"), original);

        var groups = await _finder.ScanAsync(_temp.Path);

        groups.Should().BeEmpty("a hard link is the same file, not a second copy");
    }

    /// <summary>
    /// A duplicate is handed to the Recycle Bin, never deleted outright, because the user may have picked
    /// the wrong copy. The handing-over is checked here with a stand-in: a test that used the real Recycle
    /// Bin left an item in it on every run, and nothing can reliably take an item back out - the shell's
    /// Delete verb asks for confirmation and blocks, and its Restore verb did not take effect when invoked
    /// from outside Explorer. FileScanning.SendToRecycleBin is the one line this stands in for.
    /// </summary>
    [Fact]
    public async Task ADeletedDuplicateIsHandedToTheRecycleBin()
    {
        var kept = _temp.File(@"keep\photo.txt", "a photo");
        var copy = _temp.File(@"copy\photo.txt", "a photo");

        var recycled = new List<string>();
        var finder = new DuplicateFinder(path =>
        {
            recycled.Add(path);
            File.Delete(path);
            return true;
        });

        var freed = await finder.DeleteDuplicatesAsync([copy]);

        recycled.Should().ContainSingle("the copy is the one that goes, and nothing else")
                .Which.Should().Be(copy);
        freed.Should().Be(new FileInfo(kept).Length);
        File.Exists(copy).Should().BeFalse();
        File.Exists(kept).Should().BeTrue();
    }

    [Fact]
    public async Task NothingIsCountedAsFreedWhenItCouldNotBeRemoved()
    {
        var copy = _temp.File(@"copy\photo.txt", "a photo");

        var finder = new DuplicateFinder(_ => false);
        var freed = await finder.DeleteDuplicatesAsync([copy]);

        freed.Should().Be(0, "a file that is still there has freed nothing");
        File.Exists(copy).Should().BeTrue();
    }

    [Fact]
    public async Task ALinkIsNeverDeletedAsIfItWereACopy()
    {
        var original = Path.Combine(_temp.Path, "original.txt");
        await File.WriteAllTextAsync(original, "the file itself");
        var link = Path.Combine(_temp.Path, "link.txt");
        FileSystemLinks.CreateFileSymlink(link, original);

        var freed = await _finder.DeleteDuplicatesAsync([link]);

        freed.Should().Be(0);
        File.Exists(link).Should().BeTrue("removing someone's shortcut is not freeing space");
        File.Exists(original).Should().BeTrue();
    }

    [Fact]
    public async Task TheOldestCopyIsTheOneKept()
    {
        var older = _temp.File(@"old\file.txt", "same");
        var newer = _temp.File(@"new\file.txt", "same");
        File.SetLastWriteTime(older, DateTime.Now.AddDays(-7));
        File.SetLastWriteTime(newer, DateTime.Now);

        var groups = await _finder.ScanAsync(_temp.Path);

        var original = groups.Single().Files.Single(f => f.IsOriginal);
        original.FullPath.Should().Be(older);
    }

    [Fact]
    public void TheWholeFileDecidesWhetherTwoFilesMatch()
    {
        var first = _temp.File("first.bin", new string('a', 1000) + "x" + new string('b', 1000));
        var second = _temp.File("second.bin", new string('a', 1000) + "y" + new string('b', 1000));

        DuplicateFinder.ComputeFileHash(first).Should().NotBe(DuplicateFinder.ComputeFileHash(second));
        DuplicateFinder.ComputeFileHash(first).Should().Be(DuplicateFinder.ComputeFileHash(first));
    }
}
