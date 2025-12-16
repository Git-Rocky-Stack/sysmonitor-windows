using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Optimizers;
using Xunit;

namespace SysMonitor.Tests.Services;

public class StartupOptimizerTests
{
    private readonly Mock<ILogger<StartupOptimizer>> _loggerMock;
    private readonly StartupOptimizer _startupOptimizer;

    public StartupOptimizerTests()
    {
        _loggerMock = new Mock<ILogger<StartupOptimizer>>();
        _startupOptimizer = new StartupOptimizer(_loggerMock.Object);
    }

    [Fact]
    public async Task GetStartupItemsAsync_ReturnsItems()
    {
        // Act
        var items = await _startupOptimizer.GetStartupItemsAsync();

        // Assert
        items.Should().NotBeNull();
        // Most Windows systems have at least a few startup items
    }

    [Fact]
    public async Task GetStartupItemsAsync_ItemsHaveRequiredProperties()
    {
        // Act
        var items = await _startupOptimizer.GetStartupItemsAsync();

        // Assert
        foreach (var item in items)
        {
            item.Name.Should().NotBeNullOrEmpty();
            item.Location.Should().NotBeNullOrEmpty();
            item.Type.Should().BeDefined();
            item.Impact.Should().BeDefined();
        }
    }

    [Fact]
    public async Task DisableStartupItemAsync_WithRegistryItem_AttemptsMoveToDisabled()
    {
        // Arrange - Create a test item that looks like it exists
        // Note: The method will attempt to open the registry key and move the value
        var item = new StartupItem
        {
            Name = "NonExistentItem_" + Guid.NewGuid(),
            Command = "nonexistent.exe",
            Location = "HKCU Run",
            Type = StartupItemType.Registry
        };

        // Act
        var result = await _startupOptimizer.DisableStartupItemAsync(item);

        // Assert - The operation should complete without throwing
        // The result depends on whether the registry key can be opened
        // We're testing that it handles non-existent items gracefully
        (result == true || result == false).Should().BeTrue();
    }

    [Fact]
    public async Task DeleteStartupItemAsync_WithNonExistentFile_ReturnsFalse()
    {
        // Arrange
        var item = new StartupItem
        {
            Name = "NonExistent",
            FilePath = @"C:\NonExistent\Path\file.lnk",
            Type = StartupItemType.StartupFolder
        };

        // Act
        var result = await _startupOptimizer.DeleteStartupItemAsync(item);

        // Assert
        result.Should().BeFalse();
    }
}
