using FluentAssertions;
using SysMonitor.Core.Services.Monitors;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The overlay's frame-rate figure comes from hardware most machines do not have: LibreHardwareMonitor
/// exposes exactly one frame-rate sensor, "Fullscreen FPS" on AMD GPUs whose driver has the ADL2 FrameMetrics
/// API, and it reads -1 until something is actually drawing fullscreen. So "no reading" has two different
/// meanings - no sensor at all, and a sensor with nothing to measure - and neither of them is a number. These
/// tests hold that apart, because collapsing it to an int was what put a permanent blank in the overlay.
/// </summary>
public class FrameRateTests
{
    [Fact]
    public void NoSensor_HasNoReadingAndSaysWhy()
    {
        var frameRate = FrameRate.NoSensor;

        frameRate.HasSensor.Should().BeFalse();
        frameRate.FramesPerSecond.Should().BeNull();
    }

    [Fact]
    public void Idle_HasASensorButNothingToMeasure()
    {
        var frameRate = FrameRate.Idle;

        frameRate.HasSensor.Should().BeTrue("the machine can report frame rate");
        frameRate.FramesPerSecond.Should().BeNull("nothing is drawing fullscreen");
    }

    [Fact]
    public void Of_CarriesTheMeasurement()
    {
        var frameRate = FrameRate.Of(143.5);

        frameRate.HasSensor.Should().BeTrue();
        frameRate.FramesPerSecond.Should().Be(143.5);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    public void FromSensor_TreatsTheIdleValueAsNoReading(double? sensorValue)
    {
        var frameRate = FrameRate.FromSensor(sensorValue);

        frameRate.FramesPerSecond.Should().BeNull("-1 is what the sensor reads when no game is running");
        frameRate.HasSensor.Should().BeTrue("the sensor is there either way");
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(60.0)]
    [InlineData(240.0)]
    public void FromSensor_KeepsARealReading(double sensorValue)
    {
        FrameRate.FromSensor(sensorValue).FramesPerSecond.Should().Be(sensorValue);
    }

    [Fact]
    public void AReadingAlwaysComesWithASensor()
    {
        FrameRate[] every = [FrameRate.NoSensor, FrameRate.Idle, FrameRate.Of(60), FrameRate.FromSensor(-1), FrameRate.FromSensor(75)];

        every.Where(rate => rate.FramesPerSecond.HasValue)
             .Should().OnlyContain(rate => rate.HasSensor)
             .And.HaveCount(2, "two of these are readings");
    }

    [Fact]
    public void TheDefaultIsNoReading()
    {
        // A stats object that was never filled in must not claim a frame rate of zero.
        var untouched = default(FrameRate);

        untouched.HasSensor.Should().BeFalse();
        untouched.FramesPerSecond.Should().BeNull();
    }
}
