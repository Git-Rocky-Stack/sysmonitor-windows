using FluentAssertions;
using SysMonitor.Core.Models;
using Xunit;

namespace SysMonitor.Tests.Models;

/// <summary>
/// What a result object says before anything has filled it in. It matters because the app shows these
/// straight to the user: a result that starts out claiming success would report one for work that never ran.
/// </summary>
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
}
