using FluentAssertions;
using SysMonitor.Core.Models;
using Xunit;

namespace SysMonitor.Tests.Models;

public class SystemInfoTests
{
    [Fact]
    public void SystemInfo_DefaultValues_AreInitialized()
    {
        // Act
        var info = new SystemInfo();

        // Assert
        info.Cpu.Should().NotBeNull();
        info.Memory.Should().NotBeNull();
        info.Disks.Should().NotBeNull();
        info.Network.Should().NotBeNull();
        info.OperatingSystem.Should().NotBeNull();
        info.Battery.Should().BeNull(); // Optional
        info.Timestamp.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void MemoryInfo_CalculatesGBCorrectly()
    {
        // Arrange
        var memory = new MemoryInfo
        {
            TotalBytes = 17179869184, // 16 GB
            UsedBytes = 8589934592,   // 8 GB
            AvailableBytes = 8589934592 // 8 GB
        };

        // Assert
        memory.TotalGB.Should().BeApproximately(16.0, 0.1);
        memory.UsedGB.Should().BeApproximately(8.0, 0.1);
        memory.AvailableGB.Should().BeApproximately(8.0, 0.1);
    }

    [Fact]
    public void DiskInfo_CalculatesGBCorrectly()
    {
        // Arrange
        var disk = new DiskInfo
        {
            TotalBytes = 536870912000, // 500 GB
            UsedBytes = 214748364800,  // 200 GB
            FreeBytes = 322122547200   // 300 GB
        };

        // Assert
        disk.TotalGB.Should().BeApproximately(500.0, 0.1);
        disk.UsedGB.Should().BeApproximately(200.0, 0.1);
        disk.FreeGB.Should().BeApproximately(300.0, 0.1);
    }

    /// <summary>
    /// Mbps means megabits per second, the unit a link is sold in. The counters behind it are bytes per
    /// second, so the conversion is x8 and then /1,000,000 - not /1,048,576, which is mebibytes and out by
    /// a factor of 8.4. The test that used to sit here asserted 10485760 B/s -> "10.0", certifying the bug:
    /// that rate is a 100 Mbps link running flat out, and the property called it 10.
    /// </summary>
    [Fact]
    public void NetworkInfo_ReportsMegabitsPerSecond_NotMebibytes()
    {
        var network = new NetworkInfo
        {
            UploadSpeedBps = 10_485_760,   // 10 MiB/s
            DownloadSpeedBps = 104_857_600 // 100 MiB/s
        };

        network.UploadSpeedMbps.Should().BeApproximately(83.886, 0.001);
        network.DownloadSpeedMbps.Should().BeApproximately(838.861, 0.001);
    }

    [Fact]
    public void NetworkInfo_ReportsASaturatedHundredMegabitLink_AsAHundred()
    {
        // 100 Mbps is 12,500,000 bytes per second, by definition.
        var network = new NetworkInfo { DownloadSpeedBps = 12_500_000, UploadSpeedBps = 12_500_000 };

        network.DownloadSpeedMbps.Should().BeApproximately(100.0, 0.001);
        network.UploadSpeedMbps.Should().BeApproximately(100.0, 0.001);
    }

    [Fact]
    public void NetworkInfo_ReportsAnIdleLink_AsZero()
    {
        var network = new NetworkInfo();

        network.DownloadSpeedMbps.Should().Be(0);
        network.UploadSpeedMbps.Should().Be(0);
    }

    [Fact]
    public void CpuInfo_DefaultValues_AreEmpty()
    {
        // Act
        var cpu = new CpuInfo();

        // Assert
        cpu.Name.Should().BeEmpty();
        cpu.Manufacturer.Should().BeEmpty();
        cpu.Cores.Should().Be(0);
        cpu.LogicalProcessors.Should().Be(0);
        cpu.CoreUsages.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void BatteryInfo_DefaultValues()
    {
        // Act
        var battery = new BatteryInfo();

        // Assert
        battery.IsPresent.Should().BeFalse();
        battery.IsCharging.Should().BeFalse();
        battery.ChargePercent.Should().Be(0);
        battery.HealthStatus.Should().Be("Unknown");
    }

    [Fact]
    public void OsInfo_DefaultValues()
    {
        // Act
        var os = new OsInfo();

        // Assert
        os.Name.Should().BeEmpty();
        os.Version.Should().BeEmpty();
        os.ComputerName.Should().BeEmpty();
        os.Uptime.Should().Be(TimeSpan.Zero);
    }
}
