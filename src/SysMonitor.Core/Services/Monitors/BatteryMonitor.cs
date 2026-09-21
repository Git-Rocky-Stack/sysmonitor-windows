using System.Management;
using System.Runtime.InteropServices;
using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

public class BatteryMonitor : IBatteryMonitor
{
    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    public bool HasBattery
    {
        get
        {
            if (GetSystemPowerStatus(out var status))
                return status.BatteryFlag != BatteryStatusReading.NoSystemBattery;
            return false;
        }
    }

    public async Task<BatteryInfo?> GetBatteryInfoAsync()
    {
        return await Task.Run(() =>
        {
            if (!GetSystemPowerStatus(out var status)) return null;

            // Win32 says "unknown" two different ways, and reading either as a fact is how a desktop
            // ended up being told its battery was critically low. BatteryStatusReading has the rules.
            var battery = BatteryStatusReading.Read(new BatteryPowerStatus(
                status.ACLineStatus, status.BatteryFlag, status.BatteryLifePercent, status.BatteryLifeTime));

            if (battery is null) return null;

            var capacity = ReadCapacity();
            battery.HealthStatus = capacity.Health;
            battery.DesignCapacityWh = capacity.DesignWattHours;
            battery.FullChargeCapacityWh = capacity.FullChargeWattHours;
            return battery;
        });
    }

    /// <summary>
    /// How worn the battery is: what it can hold now against what it was built to hold. Windows only reports
    /// those two figures on some machines - on the rest Win32_Battery leaves them empty - so the answer is
    /// often that this is not known, which is what it then says. It was previously the charge level, so a
    /// healthy battery at 15% was reported as being in critical health.
    /// </summary>
    private static (string Health, double DesignWattHours, double FullChargeWattHours) ReadCapacity()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DesignCapacity, FullChargeCapacity FROM Win32_Battery");

            foreach (ManagementObject battery in searcher.Get())
            {
                var design = ToWattHours(battery["DesignCapacity"]);
                var full = ToWattHours(battery["FullChargeCapacity"]);

                return (HealthFromCapacity(design, full), design, full);
            }
        }
        catch
        {
            // Best effort: a battery whose capacity Windows will not report is reported as unknown, below.
        }

        return (UnknownHealth, 0, 0);
    }

    /// <summary>Win32_Battery reports capacity in mWh where it reports it at all.</summary>
    private static double ToWattHours(object? milliWattHours) =>
        milliWattHours is null ? 0 : Convert.ToDouble(milliWattHours) / 1000.0;

    /// <summary>
    /// What a battery's capacity says about its condition: how much of the capacity it was built with it can
    /// still hold. Without both figures there is no answer, and saying so is the answer.
    /// </summary>
    public static string HealthFromCapacity(double designWattHours, double fullChargeWattHours)
    {
        if (!(designWattHours > 0) || !(fullChargeWattHours > 0))
            return UnknownHealth;

        return Math.Min(fullChargeWattHours / designWattHours, 1.0) switch
        {
            >= 0.80 => "Good",
            >= 0.60 => "Fair",
            >= 0.40 => "Worn",
            _ => "Poor",
        };
    }

    /// <summary>What the battery page shows when Windows does not report this battery's capacity.</summary>
    public const string UnknownHealth = "Not reported";
}
