using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SysMonitor.Core.Services.Monitors;
using Xunit;

namespace SysMonitor.Tests.Services;

public class CpuMonitorTests
{
    private readonly Mock<ILogger<CpuMonitor>> _loggerMock;
    private readonly CpuMonitor _cpuMonitor;

    public CpuMonitorTests()
    {
        _loggerMock = new Mock<ILogger<CpuMonitor>>();
        _cpuMonitor = new CpuMonitor(_loggerMock.Object);
    }

    [Fact]
    public async Task GetCpuInfoAsync_ReturnsValidInfo()
    {
        // Act
        var info = await _cpuMonitor.GetCpuInfoAsync();

        // Assert
        info.Should().NotBeNull();
        info.LogicalProcessors.Should().BeGreaterThan(0);
        info.Cores.Should().BeGreaterThan(0);
        info.UsagePercent.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task GetUsagePercentAsync_ReturnsValidPercentage()
    {
        // Act
        var usage = await _cpuMonitor.GetUsagePercentAsync();

        // Assert
        usage.Should().BeGreaterOrEqualTo(0);
        usage.Should().BeLessOrEqualTo(100);
    }

    [Fact]
    public async Task GetCoreUsagesAsync_ReturnsUsageForEachCore()
    {
        // Act
        var usages = await _cpuMonitor.GetCoreUsagesAsync();

        // Assert
        usages.Should().NotBeNull();
        usages.Should().HaveCount(Environment.ProcessorCount);
        usages.Should().AllSatisfy(u =>
        {
            u.Should().BeGreaterOrEqualTo(0);
            u.Should().BeLessOrEqualTo(100);
        });
    }

    [Fact]
    public void Dispose_DoesNotThrow()
    {
        // Act & Assert
        var action = () => _cpuMonitor.Dispose();
        action.Should().NotThrow();
    }
}
