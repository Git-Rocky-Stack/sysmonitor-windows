using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SysMonitor.Core.Services.Optimizers;
using Xunit;

namespace SysMonitor.Tests.Services;

public class MemoryOptimizerTests
{
    private readonly Mock<ILogger<MemoryOptimizer>> _loggerMock;
    private readonly MemoryOptimizer _memoryOptimizer;

    public MemoryOptimizerTests()
    {
        _loggerMock = new Mock<ILogger<MemoryOptimizer>>();
        _memoryOptimizer = new MemoryOptimizer(_loggerMock.Object);
    }

    [Fact]
    public async Task OptimizeMemoryAsync_ReturnsNonNegativeValue()
    {
        // Act
        var freedBytes = await _memoryOptimizer.OptimizeMemoryAsync();

        // Assert
        freedBytes.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task TrimProcessWorkingSetAsync_WithInvalidId_ReturnsZero()
    {
        // Act
        var result = await _memoryOptimizer.TrimProcessWorkingSetAsync(-1);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task ClearStandbyListAsync_ReturnsValue()
    {
        // Act
        var result = await _memoryOptimizer.ClearStandbyListAsync();

        // Assert - Currently returns 0 as it requires admin privileges
        result.Should().BeGreaterOrEqualTo(0);
    }
}
