using FluentAssertions;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The walk and the identity check the file finders rely on. A tool that offers files up for deletion must
/// not see one file twice, and must not mistake a link for a copy.
/// </summary>
public class FileScanningTests : IDisposable
{
    private readonly TempDirectory _temp = new("file-scanning");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AJunctionIsNotWalkedIntoAgain()
    {
        var data = Path.Combine(_temp.Path, "data");
        Directory.CreateDirectory(data);
        var photo = Path.Combine(data, "photo.bin");
        File.WriteAllText(photo, "one file");
        FileSystemLinks.CreateJunction(Path.Combine(_temp.Path, "data-alias"), data);

        var files = FileScanning.EnumerateFiles(_temp.Path).ToList();

        files.Should().ContainSingle().Which.Should().Be(photo);
    }

    [Fact]
    public void AFileLinkIsNotReportedAsAFile()
    {
        var original = Path.Combine(_temp.Path, "original.txt");
        File.WriteAllText(original, "the file itself");
        FileSystemLinks.CreateFileSymlink(Path.Combine(_temp.Path, "link.txt"), original);

        FileScanning.EnumerateFiles(_temp.Path).Should().ContainSingle().Which.Should().Be(original);
    }

    /// <summary>
    /// A temp clean removes the folders it emptied, and nothing else: not a folder still holding a file too new to
    /// clean, and never what a junction planted in Temp points at - the scheduled clean runs elevated.
    /// </summary>
    [Fact]
    public void OnlyEmptyFoldersAreRemovedAndAJunctionIsNotFollowed()
    {
        var elsewhere = Path.Combine(_temp.Path, "elsewhere");
        Directory.CreateDirectory(Path.Combine(elsewhere, "empty-inside"));
        var root = Path.Combine(_temp.Path, "temp");
        Directory.CreateDirectory(Path.Combine(root, "a", "b"));
        Directory.CreateDirectory(Path.Combine(root, "kept"));
        File.WriteAllText(Path.Combine(root, "kept", "in-use.tmp"), "still here");
        FileSystemLinks.CreateJunction(Path.Combine(root, "planted"), elsewhere);

        var removed = FileScanning.RemoveEmptyFolders(root);

        removed.Should().Be(2, "a and a\\b were empty");
        Directory.Exists(Path.Combine(root, "a")).Should().BeFalse();
        File.Exists(Path.Combine(root, "kept", "in-use.tmp")).Should().BeTrue();
        Directory.Exists(Path.Combine(elsewhere, "empty-inside")).Should().BeTrue("a junction is not walked into");
    }

    [Fact]
    public void EveryOrdinaryFileIsFound()
    {
        _temp.File(@"one.txt", "1");
        _temp.File(@"sub\two.txt", "2");
        _temp.File(@"sub\deeper\three.txt", "3");

        FileScanning.EnumerateFiles(_temp.Path).Should().HaveCount(3);
    }

    [Fact]
    public void TwoNamesForOneFileHaveOneIdentity()
    {
        var original = Path.Combine(_temp.Path, "original.txt");
        File.WriteAllText(original, "contents");
        var hardLink = Path.Combine(_temp.Path, "hard-link.txt");
        FileSystemLinks.CreateHardLink(hardLink, original);

        var first = FileScanning.TryGetIdentity(original);
        var second = FileScanning.TryGetIdentity(hardLink);

        first.Should().NotBeNull();
        second.Should().Be(first);
    }

    [Fact]
    public void TwoCopiesOfTheSameContentsAreDifferentFiles()
    {
        var first = _temp.File("first.txt", "identical contents");
        var second = _temp.File("second.txt", "identical contents");

        FileScanning.TryGetIdentity(first).Should().NotBe(FileScanning.TryGetIdentity(second));
    }

    [Fact]
    public void AFileThatIsNotThereHasNoIdentity() =>
        FileScanning.TryGetIdentity(Path.Combine(_temp.Path, "nothing.txt")).Should().BeNull();
}
