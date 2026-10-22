using System.IO.Compression;
using FluentAssertions;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// File Tools and PDF Tools write their results beside the user's files, so they must never write over one, and
/// must never call a result whole that is missing part of what the user asked for.
/// </summary>
public class FileToolsOutputTests : IDisposable
{
    private readonly TempDirectory _temp = new("file-tools");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task UnpackingBesideTheOriginalKeepsTheOriginal()
    {
        var original = _temp.File("report.txt", "edited since the archive");
        var archive = Path.Combine(_temp.Path, "report.txt.gz");
        await using (var target = File.Create(archive))
        await using (var gzip = new GZipStream(target, CompressionLevel.Fastest))
        await using (var writer = new StreamWriter(gzip))
        {
            await writer.WriteAsync("as archived");
        }

        var result = await new FileConverter().DecompressFileAsync(archive);

        result.Success.Should().BeTrue(result.ErrorMessage);
        File.ReadAllText(original).Should().Be("edited since the archive");
        result.OutputPath.Should().Be(Path.Combine(_temp.Path, "report (2).txt"));
        File.ReadAllText(result.OutputPath!).Should().Be("as archived");
    }

    [Fact]
    public void AFreePathIsTheFirstNameNotTaken()
    {
        _temp.File("a.zip", "1");
        _temp.File("a (2).zip", "2");

        FileConverter.FreePath(Path.Combine(_temp.Path, "a.zip")).Should().Be(Path.Combine(_temp.Path, "a (3).zip"));
        FileConverter.FreePath(Path.Combine(_temp.Path, "b.zip")).Should().Be(Path.Combine(_temp.Path, "b.zip"));
    }

    [Fact]
    public async Task AMergeWithAnUnreadablePartIsRefusedAndNamesIt()
    {
        var notAPdf = _temp.File("notes.pdf", "this is not a PDF");
        var alsoNot = _temp.File("other.pdf", "nor is this");
        var output = Path.Combine(_temp.Path, "merged.pdf");

        var result = await new PdfTools().MergePdfsAsync([notAPdf, alsoNot], output);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("notes.pdf").And.Contain("other.pdf");
        File.Exists(output).Should().BeFalse();
    }

    /// <summary>
    /// A free-space wipe is whole only when it reached the free space it set out to cover. Stopping at FAT32's
    /// 4 GiB file limit on a 60 GB card used to read as success.
    /// </summary>
    [Theory]
    [InlineData(60L * 1024 * 1024 * 1024, 4L * 1024 * 1024 * 1024, false)]
    [InlineData(10_000, 9_600, true)]
    [InlineData(10_000, 9_400, false)]
    [InlineData(0, 0, true)]
    public void AFreeSpaceWipeIsWholeOnlyWhenItCoveredTheFreeSpace(long free, long written, bool whole) =>
        DriveWiper.CoveredFreeSpace(written, free).Should().Be(whole);

    [Fact]
    public void NoFillFileOutgrowsFat32() =>
        DriveWiper.FillFileLimit.Should().BeLessThan(4L * 1024 * 1024 * 1024);
}
