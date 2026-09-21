using FluentAssertions;
using SysMonitor.Core.Services.Utilities;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The scan used to skip every entry carrying a reparse point. That is right for a junction or a symbolic
/// link — following one shows the same file twice, and a duplicate finder that believes the second one is a
/// copy deletes the only copy there is.
/// <para>
/// It is wrong for the two other things that carry a reparse point and are not links at all: a OneDrive
/// Files On-Demand placeholder, and a file stored by Data Deduplication. Both are real files, at that path,
/// with a real size. A user with 400 GB in OneDrive got no large-file results and nothing on screen to say
/// why — the scan had quietly skipped every one of them.
/// </para>
/// <para>
/// Neither of those can be created in a test, which is exactly why the decision is a function taking the
/// attributes and the link target rather than something buried in the walk. The integration tests next door
/// prove the rule is wired in, using real junctions and symbolic links.
/// </para>
/// </summary>
public class ReparsePointRuleTests
{
    private const FileAttributes Placeholder =
        FileAttributes.ReparsePoint | FileAttributes.Archive | (FileAttributes)0x400000; // RECALL_ON_DATA_ACCESS

    [Fact]
    public void ASymbolicLink_IsAWayToSomewhereElse()
    {
        FileScanning.IsLinkToElsewhere(FileAttributes.ReparsePoint, @"C:\elsewhere\real.txt")
            .Should().BeTrue();
    }

    [Fact]
    public void AJunction_IsAWayToSomewhereElse()
    {
        FileScanning.IsLinkToElsewhere(FileAttributes.ReparsePoint | FileAttributes.Directory, @"C:\elsewhere")
            .Should().BeTrue();
    }

    [Fact]
    public void AOneDrivePlaceholder_IsAFileThatIsGenuinelyHere()
    {
        FileScanning.IsLinkToElsewhere(Placeholder, linkTarget: null)
            .Should().BeFalse("it has a reparse point and points at nothing: it is the file, not a way to it");
    }

    [Fact]
    public void ADeduplicatedFile_IsAFileThatIsGenuinelyHere()
    {
        FileScanning.IsLinkToElsewhere(FileAttributes.ReparsePoint | FileAttributes.SparseFile, linkTarget: null)
            .Should().BeFalse();
    }

    [Fact]
    public void AnOrdinaryFile_IsAFileThatIsGenuinelyHere()
    {
        FileScanning.IsLinkToElsewhere(FileAttributes.Normal, linkTarget: null).Should().BeFalse();
        FileScanning.IsLinkToElsewhere(FileAttributes.Archive | FileAttributes.Hidden, null).Should().BeFalse();
    }

    [Fact]
    public void AnOrdinaryFolder_IsAFolderThatIsGenuinelyHere()
    {
        FileScanning.IsLinkToElsewhere(FileAttributes.Directory, linkTarget: null).Should().BeFalse();
    }
}
