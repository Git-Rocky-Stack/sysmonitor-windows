using FluentAssertions;
using SysMonitor.Core.Helpers;
using Xunit;

namespace SysMonitor.Tests.Helpers;

public class FormatHelperTests
{
    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(500, "500 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.00 KB")]
    [InlineData(1536, "1.50 KB")]
    [InlineData(1048576, "1.00 MB")]
    [InlineData(1073741824, "1.00 GB")]
    [InlineData(1099511627776, "1.00 TB")]
    public void FormatSize_ReturnsCorrectFormat(long bytes, string expected)
    {
        // Act
        var result = FormatHelper.FormatSize(bytes);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void FormatSize_NegativeBytes_ReturnsZero()
    {
        // Act
        var result = FormatHelper.FormatSize(-100);

        // Assert
        result.Should().Be("0 B");
    }

    [Theory]
    [InlineData(0, "0 B/s")]
    [InlineData(500, "500 B/s")]
    [InlineData(1000, "1.0 KB/s")]
    [InlineData(1500000, "1.5 MB/s")]
    [InlineData(1500000000, "1.5 GB/s")]
    public void FormatSpeed_ReturnsCorrectFormat(double bytesPerSecond, string expected)
    {
        // Act
        var result = FormatHelper.FormatSpeed(bytesPerSecond);

        // Assert
        result.Should().Be(expected);
    }

    [Fact]
    public void FormatUptime_LessThanHour_ReturnsMinutes()
    {
        // Arrange
        var uptime = TimeSpan.FromMinutes(45);

        // Act
        var result = FormatHelper.FormatUptime(uptime);

        // Assert
        result.Should().Be("45m");
    }

    [Fact]
    public void FormatUptime_LessThanDay_ReturnsHoursAndMinutes()
    {
        // Arrange
        var uptime = TimeSpan.FromHours(5) + TimeSpan.FromMinutes(30);

        // Act
        var result = FormatHelper.FormatUptime(uptime);

        // Assert
        result.Should().Be("5h 30m");
    }

    [Fact]
    public void FormatUptime_MoreThanDay_ReturnsDaysHoursMinutes()
    {
        // Arrange
        var uptime = TimeSpan.FromDays(3) + TimeSpan.FromHours(12) + TimeSpan.FromMinutes(15);

        // Act
        var result = FormatHelper.FormatUptime(uptime);

        // Assert
        result.Should().Be("3d 12h 15m");
    }

    [Theory]
    [InlineData(0, "0.0%")]
    [InlineData(50.5, "50.5%")]
    [InlineData(100, "100.0%")]
    public void FormatPercent_ReturnsCorrectFormat(double percent, string expected)
    {
        // Act
        var result = FormatHelper.FormatPercent(percent);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "N/A")]
    [InlineData(-10, "N/A")]
    [InlineData(double.NaN, "N/A")]
    [InlineData(25, "77°F")]
    [InlineData(100, "212°F")]
    public void FormatTemperatureF_ReturnsCorrectFormat(double celsius, string expected)
    {
        // Act
        var result = FormatHelper.FormatTemperatureF(celsius);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "N/A")]
    [InlineData(double.NaN, "N/A")]
    [InlineData(25, "25°C")]
    [InlineData(100, "100°C")]
    public void FormatTemperatureC_ReturnsCorrectFormat(double celsius, string expected)
    {
        // Act
        var result = FormatHelper.FormatTemperatureC(celsius);

        // Assert
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "--")]
    [InlineData(-10, "--")]
    [InlineData(double.NaN, "--")]
    [InlineData(25, "77")]
    [InlineData(100, "212")]
    public void FormatTemperatureF_WithoutUnit_ShowsNoReadingAsAPlaceholder(double celsius, string expected)
    {
        // Act
        var result = FormatHelper.FormatTemperatureF(celsius, showUnit: false);

        // Assert
        result.Should().Be(expected,
            "a caller that draws its own unit label was handed \"0\" for a missing sensor, which reads as a measurement");
    }

    [Theory]
    [InlineData(0, "--")]
    [InlineData(double.NaN, "--")]
    [InlineData(25, "25")]
    public void FormatTemperatureC_WithoutUnit_ShowsNoReadingAsAPlaceholder(double celsius, string expected)
    {
        // Act
        var result = FormatHelper.FormatTemperatureC(celsius, showUnit: false);

        // Assert
        result.Should().Be(expected);
    }
}
