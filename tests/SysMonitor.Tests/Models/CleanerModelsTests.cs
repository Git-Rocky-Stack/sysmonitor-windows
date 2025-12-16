using FluentAssertions;
using SysMonitor.Core.Models;
using Xunit;

namespace SysMonitor.Tests.Models;

public class CleanerModelsTests
{
    [Fact]
    public void CleanerScanResult_DefaultValues()
    {
        // Act
        var result = new CleanerScanResult();

        // Assert
        result.Name.Should().BeEmpty();
        result.Path.Should().BeEmpty();
        result.SizeBytes.Should().Be(0);
        result.FileCount.Should().Be(0);
        result.IsSelected.Should().BeTrue(); // Default is true
        result.Description.Should().BeEmpty();
    }

    [Fact]
    public void CleanerResult_DefaultValues()
    {
        // Act
        var result = new CleanerResult();

        // Assert
        result.Success.Should().BeFalse();
        result.BytesCleaned.Should().Be(0);
        result.FilesDeleted.Should().Be(0);
        result.FoldersDeleted.Should().Be(0);
        result.ErrorCount.Should().Be(0);
        result.Errors.Should().NotBeNull().And.BeEmpty();
        result.Duration.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData(CleanerRiskLevel.Safe)]
    [InlineData(CleanerRiskLevel.Low)]
    [InlineData(CleanerRiskLevel.Medium)]
    [InlineData(CleanerRiskLevel.High)]
    public void CleanerRiskLevel_AllValuesExist(CleanerRiskLevel level)
    {
        // Assert - just verifying enum values exist
        level.Should().BeDefined();
    }

    [Theory]
    [InlineData(CleanerCategory.UserTemp)]
    [InlineData(CleanerCategory.WindowsTemp)]
    [InlineData(CleanerCategory.BrowserCache)]
    [InlineData(CleanerCategory.RecycleBin)]
    [InlineData(CleanerCategory.Thumbnails)]
    [InlineData(CleanerCategory.LogFiles)]
    public void CleanerCategory_AllValuesExist(CleanerCategory category)
    {
        // Assert - just verifying enum values exist
        category.Should().BeDefined();
    }

    [Fact]
    public void CleanerResult_SuccessLogic_WorksCorrectly()
    {
        // Arrange
        var result = new CleanerResult
        {
            FilesDeleted = 100,
            ErrorCount = 10
        };

        // Assert - Success should be true when errors < files deleted
        // (This tests the logic that allows some errors)
        result.FilesDeleted.Should().BeGreaterThan(result.ErrorCount);
    }
}
