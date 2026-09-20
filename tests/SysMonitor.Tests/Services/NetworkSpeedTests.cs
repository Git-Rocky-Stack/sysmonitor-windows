using FluentAssertions;
using SysMonitor.Core.Services.Monitors;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Two things put 0 B/s on the dashboard: the reading was taken from whichever adapter claimed the fastest
/// link - a WSL or Hyper-V switch that carries nothing - and two pages asking during the same refresh left
/// one of them with a zero.
/// </summary>
public class NetworkSpeedTests
{
    private const string WiFi = "{wifi}";
    private const string Wsl = "{wsl}";

    [Fact]
    public void TheAdapterCarryingTheTrafficIsChosenOverTheOneClaimingTheFastestLink()
    {
        // The machine in the review: a 300 Mb/s Wi-Fi with the default route, and a WSL switch claiming 10 Gb/s.
        var chosen = NetworkMonitor.ChooseActive(
        [
            new NetworkMonitor.AdapterChoice(Wsl, IsUp: true, IsLoopbackOrTunnel: false, OwnsDefaultRoute: false, HasDefaultGateway: false, Speed: 10_000_000_000),
            new NetworkMonitor.AdapterChoice(WiFi, IsUp: true, IsLoopbackOrTunnel: false, OwnsDefaultRoute: true, HasDefaultGateway: true, Speed: 300_000_000),
        ]);

        chosen!.Value.Id.Should().Be(WiFi);
    }

    [Fact]
    public void AGatewayDecidesWhenWindowsWillNotSayWhichInterfaceItRoutesThrough()
    {
        var chosen = NetworkMonitor.ChooseActive(
        [
            new NetworkMonitor.AdapterChoice(Wsl, true, false, OwnsDefaultRoute: false, HasDefaultGateway: false, Speed: 10_000_000_000),
            new NetworkMonitor.AdapterChoice(WiFi, true, false, OwnsDefaultRoute: false, HasDefaultGateway: true, Speed: 300_000_000),
        ]);

        chosen!.Value.Id.Should().Be(WiFi);
    }

    [Fact]
    public void WithNothingToChooseBetweenTheFasterLinkWins()
    {
        var chosen = NetworkMonitor.ChooseActive(
        [
            new NetworkMonitor.AdapterChoice("slow", true, false, false, false, 100_000_000),
            new NetworkMonitor.AdapterChoice("fast", true, false, false, false, 1_000_000_000),
        ]);

        chosen!.Value.Id.Should().Be("fast");
    }

    [Fact]
    public void AdaptersThatCannotBeCarryingTrafficAreNotConsidered()
    {
        NetworkMonitor.ChooseActive(
        [
            new NetworkMonitor.AdapterChoice("down", IsUp: false, IsLoopbackOrTunnel: false, OwnsDefaultRoute: true, HasDefaultGateway: true, Speed: 1_000_000_000),
            new NetworkMonitor.AdapterChoice("loopback", IsUp: true, IsLoopbackOrTunnel: true, OwnsDefaultRoute: true, HasDefaultGateway: true, Speed: 1_000_000_000),
        ]).Should().BeNull();

        NetworkMonitor.ChooseActive([]).Should().BeNull();
    }

    [Fact]
    public void TheFirstReadingHasNothingToCompareWith()
    {
        var sampler = new NetworkSpeedSampler();

        sampler.Sample(WiFi, 1000, 2000, At(0)).Should().Be((0d, 0d));
    }

    [Fact]
    public void ASecondReadingGivesTheRateBetweenTheTwo()
    {
        var sampler = new NetworkSpeedSampler();
        sampler.Sample(WiFi, 1_000, 2_000, At(0));

        var (upload, download) = sampler.Sample(WiFi, 3_000, 12_000, At(2));

        upload.Should().Be(1_000, "2,000 bytes went out over two seconds");
        download.Should().Be(5_000);
    }

    [Fact]
    public void EveryoneAskingDuringTheSameRefreshGetsTheSameAnswer()
    {
        var sampler = new NetworkSpeedSampler();
        sampler.Sample(WiFi, 0, 0, At(0));
        var first = sampler.Sample(WiFi, 1_000, 10_000, At(1));

        // The dashboard, the network page and the history all read within the same tick.
        var second = sampler.Sample(WiFi, 1_010, 10_100, At(1.05));
        var third = sampler.Sample(WiFi, 1_020, 10_200, At(1.1));

        first.Download.Should().Be(10_000);
        second.Should().Be(first, "the second caller used to get a zero");
        third.Should().Be(first);
    }

    [Fact]
    public void AReadingFromAnotherAdapterStartsAgainRatherThanInventingASpike()
    {
        var sampler = new NetworkSpeedSampler();
        sampler.Sample(WiFi, 0, 0, At(0));
        sampler.Sample(WiFi, 1_000, 1_000, At(1));

        // The machine moved to the cable, whose counters have been running since boot.
        sampler.Sample("{ethernet}", 900_000_000, 900_000_000, At(2)).Should().Be((0d, 0d));
        sampler.Sample("{ethernet}", 900_001_000, 900_002_000, At(3)).Should().Be((1_000d, 2_000d));
    }

    [Fact]
    public void CountersThatRestartDoNotBecomeASpike()
    {
        var sampler = new NetworkSpeedSampler();
        sampler.Sample(WiFi, 5_000, 5_000, At(0));

        // The adapter came back up and its counters started from zero again.
        sampler.Sample(WiFi, 10, 20, At(1)).Should().Be((0d, 0d));
        sampler.Sample(WiFi, 110, 220, At(2)).Should().Be((100d, 200d));
    }

    [Fact]
    public void ReadingsFromSeveralThreadsAtOnceStayConsistent()
    {
        var sampler = new NetworkSpeedSampler();
        sampler.Sample(WiFi, 0, 0, At(0));

        var results = new System.Collections.Concurrent.ConcurrentBag<(double Upload, double Download)>();
        Parallel.For(0, 64, i => results.Add(sampler.Sample(WiFi, 1_000, 10_000, At(1))));

        // Whichever call computes the rate, the rest see that same answer - never a zero and never a negative.
        results.Should().OnlyContain(r => r.Download == 0 || r.Download == 10_000);
        results.Should().Contain(r => r.Download == 10_000);
        results.Should().OnlyContain(r => r.Upload >= 0 && r.Download >= 0);
    }

    private static DateTime At(double seconds) => new DateTime(2026, 9, 19, 12, 0, 0, DateTimeKind.Utc).AddSeconds(seconds);
}
