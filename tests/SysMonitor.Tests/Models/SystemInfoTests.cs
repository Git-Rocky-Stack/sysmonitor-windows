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

    [Fact]
    public void NetworkInfo_CalculatesMbpsCorrectly()
    {
        // Arrange
        var network = new NetworkInfo
        {
            UploadSpeedBps = 10485760,   // 10 MB/s
            DownloadSpeedBps = 104857600 // 100 MB/s
        };

        // Assert
        network.UploadSpeedMbps.Should().BeApproximately(10.0, 0.1);
        network.DownloadSpeedMbps.Should().BeApproximately(100.0, 0.1);
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
