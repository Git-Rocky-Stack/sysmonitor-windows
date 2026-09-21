using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

/// <summary>What <c>GetSystemPowerStatus</c> filled in, as plain values.</summary>
public readonly record struct BatteryPowerStatus(
    byte AcLineStatus,
    byte BatteryFlag,
    byte BatteryLifePercent,
    int BatteryLifeTimeSeconds);

/// <summary>
/// Reads a <see cref="BatteryPowerStatus"/> into a <see cref="BatteryInfo"/>, or into nothing when there is
/// nothing to say.
///
/// <para>
/// Win32 has two separate ways of saying "I do not know", and both used to be read as facts.
/// <c>BatteryFlag</c> 255 means unknown — only 128 means no battery — so a machine whose firmware answered
/// 255 was reported as having one. <c>BatteryLifePercent</c> 255 also means unknown, and the old reading
/// turned anything above 100 into <b>0</b>, which the alert service compares against the critical
/// threshold. The result was "Battery Critical! Battery is at 0% - plug in immediately!" on a desktop.
/// </para>
/// </summary>
public static class BatteryStatusReading
{
    /// <summary>BatteryFlag: no system battery.</summary>
    public const byte NoSystemBattery = 128;

    /// <summary>BatteryFlag and BatteryLifePercent: the value is unknown.</summary>
    public const byte UnknownValue = 255;

    /// <summary>BatteryFlag: the battery is charging.</summary>
    private const byte ChargingFlag = 8;

    /// <summary>The battery this status describes, or null when it describes no battery, or nothing usable.</summary>
    public static BatteryInfo? Read(BatteryPowerStatus status)
    {
        if (status.BatteryFlag == NoSystemBattery)
            return null;

        // Without a charge level there is nothing worth showing, and nothing safe to show: a battery
        // reported at an invented 0% is one the alert service calls critical.
        if (status.BatteryLifePercent > 100)
            return null;

        return new BatteryInfo
        {
            IsPresent = true,
            IsPluggedIn = status.AcLineStatus == 1,

            // The charging bit is meaningless when the whole flag reads "unknown".
            IsCharging = status.BatteryFlag != UnknownValue && (status.BatteryFlag & ChargingFlag) != 0,

            ChargePercent = status.BatteryLifePercent,

            // Windows uses -1 for "no estimate"; zero is how this app already says the same thing.
            EstimatedRuntime = status.BatteryLifeTimeSeconds > 0
                ? TimeSpan.FromSeconds(status.BatteryLifeTimeSeconds)
                : TimeSpan.Zero,
        };
    }
}
