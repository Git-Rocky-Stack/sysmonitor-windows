using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The large-file scan splits the work across top-level folders to go faster. The list it split over was
/// <c>{ root } ∪ root's subfolders</c>, and <c>FileScanning.EnumerateFiles</c> already recurses — so every
/// file below the root was walked twice, once under the root and once under its own folder.
/// <para>
/// The cost was double the disk reads. The visible fault was worse: the same file appeared twice in the
/// results, and the total it reports is the sum of the rows.
/// </para>
/// </summary>
public class LargeFileScanTests : IDisposable
{
    private const long OneMegabyte = 1024 * 1024;

    private readonly TempDirectory _temp = new("large-files");
    private readonly LargeFileFinder _finder = new(Mock.Of<ILogger<LargeFileFinder>>());

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task AFileInASubfolder_IsFoundOnce()
    {
        WriteFile("deep/nested/big.bin", 3 * OneMegabyte);

        var found = await _finder.ScanAsync(_temp.Path, minSizeBytes: OneMegabyte);

        found.Should().ContainSingle("the same file walked twice is the same file")
            .Which.FileName.Should().Be("big.bin");
    }

    [Fact]
    public async Task AFileDirectlyInTheRoot_IsFoundOnce()
    {
        WriteFile("top.bin", 3 * OneMegabyte);

        var found = await _finder.ScanAsync(_temp.Path, minSizeBytes: OneMegabyte);

        found.Should().ContainSingle().Which.FileName.Should().Be("top.bin");
    }

    [Fact]
    public async Task EveryLargeFileIsFound_WhereverItSits()
    {
        WriteFile("top.bin", 3 * OneMegabyte);
        WriteFile("one/a.bin", 3 * OneMegabyte);
        WriteFile("one/two/b.bin", 3 * OneMegabyte);
        WriteFile("three/c.bin", 3 * OneMegabyte);
        WriteFile("three/small.bin", 1024);          // under the threshold

        var found = await _finder.ScanAsync(_temp.Path, minSizeBytes: OneMegabyte);

        found.Select(file => file.FileName).Should().BeEquivalentTo(["top.bin", "a.bin", "b.bin", "c.bin"]);
        found.Select(file => file.FullPath).Should().OnlyHaveUniqueItems("nothing is reported twice");
    }

    [Fact]
    public async Task TheTotalIsTheSizeOnDisk_NotTheSizeTimesHowOftenItWasWalked()
    {
        WriteFile("one/a.bin", 4 * OneMegabyte);
        WriteFile("one/two/b.bin", 4 * OneMegabyte);

        var found = await _finder.ScanAsync(_temp.Path, minSizeBytes: OneMegabyte);

        found.Sum(file => file.SizeBytes).Should().Be(8 * OneMegabyte);
    }

    private void WriteFile(string relativePath, long bytes)
    {
        var path = Path.Combine(_temp.Path, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        stream.SetLength(bytes);
    }
}
