using FluentAssertions;
using SysMonitor.Core.Helpers;
using Xunit;

namespace SysMonitor.Tests.Helpers;

/// <summary>
/// Staying inside the folder the user picked is what stops a wipe walking into the rest of the disk and a
/// restore writing outside the destination. Both of those call this, so what it decides here is what they do.
/// </summary>
public class PathHelperTests
{
    [Theory]
    [InlineData(@"C:\Users\Test\file.txt", @"C:\Users\Test", true)]
    [InlineData(@"C:\Users\Test\Sub\file.txt", @"C:\Users\Test", true)]
    [InlineData(@"C:\Users\Test\", @"C:\Users\Test", false)]
    [InlineData(@"C:\Users\Test", @"C:\Users\Test", false)]
    [InlineData(@"C:\Users\Other\file.txt", @"C:\Users\Test", false)]
    [InlineData(@"C:\Users\Testing\file.txt", @"C:\Users\Test", false)]
    [InlineData(@"D:\Other\file.txt", @"C:\Users\Test", false)]
    public void IsPathWithinDirectory_ValidatesContainment(string path, string basePath, bool expected)
    {
        PathHelper.IsPathWithinDirectory(path, basePath).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\Users\Test\..\Other\file.txt")]
    [InlineData(@"C:\Users\Test\Sub\..\..\Other\file.txt")]
    public void IsPathWithinDirectory_ResolvesAPathBeforeJudgingIt(string path)
    {
        PathHelper.IsPathWithinDirectory(path, @"C:\Users\Test")
            .Should().BeFalse("it leaves the folder, however it is spelled");
    }

    [Fact]
    public void IsPathWithinDirectory_AcceptsADeeperPathThatOnlyLooksLikeItLeaves()
    {
        PathHelper.IsPathWithinDirectory(@"C:\Users\Test\Sub\..\Other\file.txt", @"C:\Users\Test")
            .Should().BeTrue("it comes back inside");
    }

    [Theory]
    [InlineData(@"c:\users\test\file.txt", @"C:\Users\Test")]
    [InlineData(@"C:\USERS\TEST\FILE.TXT", @"c:\users\test")]
    public void IsPathWithinDirectory_IgnoresCase(string path, string basePath)
    {
        PathHelper.IsPathWithinDirectory(path, basePath).Should().BeTrue("Windows paths are case-insensitive");
    }

    [Theory]
    [InlineData(null, @"C:\Users\Test")]
    [InlineData("", @"C:\Users\Test")]
    [InlineData("   ", @"C:\Users\Test")]
    [InlineData(@"C:\Users\Test\file.txt", null)]
    [InlineData(@"C:\Users\Test\file.txt", "")]
    [InlineData("\0", @"C:\Users\Test")]
    public void IsPathWithinDirectory_RefusesWhatItCannotJudge(string? path, string? basePath)
    {
        PathHelper.IsPathWithinDirectory(path, basePath)
            .Should().BeFalse("an answer it cannot work out is not a yes");
    }

    [Theory]
    [InlineData(@"C:\Users\Test", @"C:\Users\Test", true)]
    [InlineData(@"C:\Users\Test\", @"C:\Users\Test", true)]
    [InlineData(@"c:\users\test", @"C:\Users\Test", true)]
    [InlineData(@"C:\Users\Test\Sub", @"C:\Users\Test", false)]
    [InlineData(@"C:\Users\Testing", @"C:\Users\Test", false)]
    [InlineData(null, @"C:\Users\Test", false)]
    public void IsSamePath_ComparesTheSameWayWindowsDoes(string? path, string? other, bool expected)
    {
        PathHelper.IsSamePath(path, other).Should().Be(expected);
    }
}
