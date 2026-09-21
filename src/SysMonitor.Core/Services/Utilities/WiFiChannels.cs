namespace SysMonitor.Core.Services.Utilities;

/// <summary>The radio band a network is on, or that it is not known which.</summary>
public enum WiFiBand
{
    Unknown,
    TwoPointFourGhz,
    FiveGhz,
    SixGhz,
}

/// <summary>
/// Turning between channel numbers and frequencies, and saying which band a network is on.
///
/// <para>
/// Channel numbers are not unique. 6 GHz (Wi-Fi 6E) numbers its channels from 1 again, so channel 5 is
/// either a 2.4 GHz channel or a 6 GHz one and nothing about the number says which. A frequency is
/// unambiguous; a channel number on its own sometimes is not, and this says so rather than guessing.
/// </para>
/// </summary>
public static class WiFiChannels
{
    // 2.4 GHz: channel 1 is 2412 MHz, 5 MHz apart, and channel 14 is the exception at 2484.
    private const int TwoPointFourFirstMhz = 2412;
    private const int Channel14Mhz = 2484;

    // 5 GHz: centre = 5000 + 5 x channel.
    private const int FiveGhzBaseMhz = 5000;

    // 6 GHz: centre = 5950 + 5 x channel, so channel 1 is 5955 MHz.
    private const int SixGhzBaseMhz = 5950;

    private const int SixGhzLowMhz = 5925;
    private const int SixGhzHighMhz = 7125;

    /// <summary>The band a frequency in MHz belongs to. A frequency is never ambiguous.</summary>
    public static WiFiBand BandFromFrequency(int frequencyMhz) => frequencyMhz switch
    {
        >= 2400 and <= 2500 => WiFiBand.TwoPointFourGhz,
        >= 5150 and <= 5895 => WiFiBand.FiveGhz,
        >= SixGhzLowMhz and <= SixGhzHighMhz => WiFiBand.SixGhz,
        _ => WiFiBand.Unknown,
    };

    /// <summary>
    /// The band a channel number belongs to, where the number alone settles it.
    /// <para>
    /// 15 to 31 and 234 upwards are not channels at all. 1 to 14 could be 2.4 GHz or 6 GHz and 32 to 233
    /// could be 5 GHz or 6 GHz, so those say what they can and no more — a label reading "5 GHz" for a
    /// 6 GHz network is worse than one reading "Unknown".
    /// </para>
    /// </summary>
    public static WiFiBand BandFromChannel(int channel) => channel switch
    {
        >= 1 and <= 14 => WiFiBand.TwoPointFourGhz,
        >= 32 and <= 177 => WiFiBand.FiveGhz,
        _ => WiFiBand.Unknown,
    };

    /// <summary>The channel number for a frequency in MHz, or 0 when it is not a Wi-Fi frequency.</summary>
    public static int ChannelFromFrequency(int frequencyMhz)
    {
        if (frequencyMhz == Channel14Mhz)
            return 14;

        return BandFromFrequency(frequencyMhz) switch
        {
            WiFiBand.TwoPointFourGhz when frequencyMhz >= TwoPointFourFirstMhz =>
                ((frequencyMhz - TwoPointFourFirstMhz) / 5) + 1,
            WiFiBand.FiveGhz => (frequencyMhz - FiveGhzBaseMhz) / 5,
            WiFiBand.SixGhz => (frequencyMhz - SixGhzBaseMhz) / 5,
            _ => 0,
        };
    }

    /// <summary>The centre frequency in MHz for a channel on a given band, or 0 when there is no such channel.</summary>
    public static int FrequencyFromChannel(int channel, WiFiBand band)
    {
        if (channel <= 0)
            return 0;

        return band switch
        {
            WiFiBand.TwoPointFourGhz when channel == 14 => Channel14Mhz,
            WiFiBand.TwoPointFourGhz when channel <= 13 => TwoPointFourFirstMhz + ((channel - 1) * 5),
            WiFiBand.FiveGhz when channel is >= 32 and <= 177 => FiveGhzBaseMhz + (channel * 5),
            WiFiBand.SixGhz when channel is >= 1 and <= 233 => SixGhzBaseMhz + (channel * 5),
            _ => 0,
        };
    }

    /// <summary>The band as the interface shows it.</summary>
    public static string Describe(WiFiBand band) => band switch
    {
        WiFiBand.TwoPointFourGhz => "2.4 GHz",
        WiFiBand.FiveGhz => "5 GHz",
        WiFiBand.SixGhz => "6 GHz",
        _ => "Unknown",
    };
}
