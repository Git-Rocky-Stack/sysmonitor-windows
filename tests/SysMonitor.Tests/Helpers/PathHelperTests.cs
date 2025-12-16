using FluentAssertions;
using SysMonitor.Core.Helpers;
using Xunit;

namespace SysMonitor.Tests.Helpers;

public class PathHelperTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(@"C:\Valid\Path", true)]
    [InlineData(@"C:\Valid\Path\file.txt", true)]
    public void IsPathSafe_BasicValidation(string? path, bool expected)
    {
        // Act
        var result = PathHelper.IsPathSafe(path);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\Test\..\Windows\System32", false)]
    [InlineData(@"C:\Test\..\..\Windows", false)]
    [InlineData(@"..\secret.txt", false)]
    public void IsPathSafe_RejectsDirectoryTraversal(string path, bool expected)
    {
        // Act
        var result = PathHelper.IsPathSafe(path);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(@"C:\Users\Test\file.txt", @"C:\Users\Test", true)]
    [InlineData(@"C:\Users\Test\Sub\file.txt", @"C:\Users\Test", true)]
    [InlineData(@"C:\Users\Other\file.txt", @"C:\Users\Test", false)]
    [InlineData(@"D:\Other\file.txt", @"C:\Users\Test", false)]
    public void IsPathWithinDirectory_ValidatesContainment(string path, string basePath, bool expected)
    {
        // Act
        var result = PathHelper.IsPathWithinDirectory(path, basePath);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(null, "unnamed")]
    [InlineData("", "unnamed")]
    [InlineData("valid_file.txt", "valid_file.txt")]
    [InlineData("file<with>invalid:chars?.txt", "file_with_invalid_chars_.txt")]
    [InlineData("file/with\\slashes.txt", "file_with_slashes.txt")]
    public void GetSafeFileName_SanitizesInput(string? input, string expected)
    {
        // Act
        var result = PathHelper.GetSafeFileName(input);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void GetDirectorySize_NonExistentDirectory_ReturnsZero()
    {
        // Arrange
        var path = @"C:\NonExistent\Directory\Path\That\Should\Not\Exist";

        // Act
        var (size, count) = PathHelper.GetDirectorySize(path);

        // Assert
        size.Should().Be(0);
        count.Should().Be(0);
    }

    [Fact]
    public void GetDirectorySize_TempDirectory_ReturnsValues()
    {
        // Arrange
        var tempPath = Path.GetTempPath();

        // Act
        var (size, count) = PathHelper.GetDirectorySize(tempPath, includeSubdirectories: false);

        // Assert - temp directory should have some files
        // We just verify it doesn't throw and returns reasonable values
        size.Should().BeGreaterOrEqualTo(0);
        count.Should().BeGreaterOrEqualTo(0);
    }
}
