using FluentAssertions;
using Moq;
using SysMonitor.Core.Services.History;
using SysMonitor.Core.Services.Monitors;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The window disposes the history service when it closes and the host disposes it again on exit. The second call
/// used to throw from a disposed token source, which ended the host's dispose loop and left every service built
/// before this one - the hardware monitor's driver among them - open.
/// </summary>
public class HistoryServiceLifetimeTests
{
    [Fact]
    public void DisposingTwiceIsHarmless()
    {
        var service = new HistoryService(
            Mock.Of<ICpuMonitor>(), Mock.Of<IMemoryMonitor>(), Mock.Of<ITemperatureMonitor>(),
            Mock.Of<INetworkMonitor>(), Mock.Of<IBatteryMonitor>(), Mock.Of<IDiskMonitor>());

        service.Dispose();
        var again = () => service.Dispose();

        again.Should().NotThrow();
        service.Invoking(s => s.StopRecording()).Should().NotThrow();
    }
}
