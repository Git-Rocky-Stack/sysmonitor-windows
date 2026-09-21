using FluentAssertions;
using SysMonitor.Core.Services.Utilities;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Wi-Fi 6E put a third band in play and numbered its channels from 1 again, which breaks two assumptions
/// the analyser was built on: that a channel above 14 is a 5 GHz channel, and that a channel number on its
/// own identifies a network's band at all.
/// <para>
/// The 6 GHz arithmetic was also out by one — <c>(mhz - 5950) / 5 + 1</c> calls 5955 MHz channel 2 when it
/// is channel 1 — and the reverse conversion had no 6 GHz case, so a 6 GHz channel came back as no
/// frequency at all.
/// </para>
/// </summary>
public class WiFiChannelTests
{
    [Theory]
    [InlineData(2412, 1)]
    [InlineData(2437, 6)]
    [InlineData(2462, 11)]
    [InlineData(2484, 14)]   // Japan's odd one out: 12 MHz above channel 13, not 5.
    public void ATwoPointFourGhzFrequency_GivesItsChannel(int mhz, int channel)
    {
        WiFiChannels.ChannelFromFrequency(mhz).Should().Be(channel);
        WiFiChannels.BandFromFrequency(mhz).Should().Be(WiFiBand.TwoPointFourGhz);
    }

    [Theory]
    [InlineData(5180, 36)]
    [InlineData(5320, 64)]
    [InlineData(5500, 100)]
    [InlineData(5825, 165)]
    public void AFiveGhzFrequency_GivesItsChannel(int mhz, int channel)
    {
        WiFiChannels.ChannelFromFrequency(mhz).Should().Be(channel);
        WiFiChannels.BandFromFrequency(mhz).Should().Be(WiFiBand.FiveGhz);
    }

    [Theory]
    [InlineData(5955, 1)]    // The first 6 GHz channel. 5950 + 5 x 1.
    [InlineData(5975, 5)]
    [InlineData(6175, 45)]
    [InlineData(7115, 233)]  // The last one.
    public void ASixGhzFrequency_GivesItsChannel(int mhz, int channel)
    {
        WiFiChannels.ChannelFromFrequency(mhz).Should().Be(channel);
        WiFiChannels.BandFromFrequency(mhz).Should().Be(WiFiBand.SixGhz);
    }

    [Theory]
    [InlineData(1, WiFiBand.TwoPointFourGhz, 2412)]
    [InlineData(13, WiFiBand.TwoPointFourGhz, 2472)]
    [InlineData(14, WiFiBand.TwoPointFourGhz, 2484)]
    [InlineData(36, WiFiBand.FiveGhz, 5180)]
    [InlineData(165, WiFiBand.FiveGhz, 5825)]
    [InlineData(1, WiFiBand.SixGhz, 5955)]
    [InlineData(233, WiFiBand.SixGhz, 7115)]
    public void AChannelOnAKnownBand_GivesItsFrequency(int channel, WiFiBand band, int mhz)
    {
        WiFiChannels.FrequencyFromChannel(channel, band).Should().Be(mhz);
    }

    [Fact]
    public void EveryChannelSurvivesTheRoundTrip()
    {
        foreach (var band in new[] { WiFiBand.TwoPointFourGhz, WiFiBand.FiveGhz, WiFiBand.SixGhz })
        {
            for (var channel = 1; channel <= 233; channel++)
            {
                var mhz = WiFiChannels.FrequencyFromChannel(channel, band);
                if (mhz == 0)
                    continue;   // not a channel on this band

                WiFiChannels.ChannelFromFrequency(mhz).Should().Be(channel,
                    $"channel {channel} on {band} is {mhz} MHz and must read back as itself");
                WiFiChannels.BandFromFrequency(mhz).Should().Be(band);
            }
        }
    }

    [Theory]
    [InlineData(1, WiFiBand.TwoPointFourGhz)]
    [InlineData(11, WiFiBand.TwoPointFourGhz)]
    [InlineData(36, WiFiBand.FiveGhz)]
    [InlineData(165, WiFiBand.FiveGhz)]
    public void AChannelNumberThatSettlesTheBand_SaysWhichBand(int channel, WiFiBand band)
    {
        WiFiChannels.BandFromChannel(channel).Should().Be(band);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]    // between the bands: not a channel on any of them
    [InlineData(200)]   // a 6 GHz channel, and nothing else
    [InlineData(400)]
    [InlineData(-1)]
    public void AChannelNumberThatSettlesNothing_SaysUnknown(int channel)
    {
        WiFiChannels.BandFromChannel(channel).Should().Be(WiFiBand.Unknown,
            "a label reading \"5 GHz\" for a network that is not on it is worse than one reading Unknown");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1000)]
    [InlineData(5000)]
    [InlineData(8000)]
    public void AFrequencyThatIsNotWiFi_HasNoChannelAndNoBand(int mhz)
    {
        WiFiChannels.ChannelFromFrequency(mhz).Should().Be(0);
        WiFiChannels.BandFromFrequency(mhz).Should().Be(WiFiBand.Unknown);
    }

    [Theory]
    [InlineData(WiFiBand.TwoPointFourGhz, "2.4 GHz")]
    [InlineData(WiFiBand.FiveGhz, "5 GHz")]
    [InlineData(WiFiBand.SixGhz, "6 GHz")]
    [InlineData(WiFiBand.Unknown, "Unknown")]
    public void TheBandReadsAsTheInterfaceShowsIt(WiFiBand band, string shown)
    {
        WiFiChannels.Describe(band).Should().Be(shown);
    }
}
