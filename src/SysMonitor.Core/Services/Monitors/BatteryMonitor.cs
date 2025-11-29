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
                return status.BatteryFlag != 128;
            return false;
        }
    }

    public async Task<BatteryInfo?> GetBatteryInfoAsync()
    {
        return await Task.Run(() =>
        {
            if (!GetSystemPowerStatus(out var status)) return null;
            if (status.BatteryFlag == 128) return null;

            return new BatteryInfo
            {
                IsPresent = true,
                IsPluggedIn = status.ACLineStatus == 1,
                IsCharging = (status.BatteryFlag & 8) != 0,
                ChargePercent = status.BatteryLifePercent <= 100 ? status.BatteryLifePercent : 0,
                EstimatedRuntime = status.BatteryLifeTime > 0
                    ? TimeSpan.FromSeconds(status.BatteryLifeTime)
                    : TimeSpan.Zero,
                HealthStatus = GetHealthStatus(status.BatteryLifePercent)
            };
        });
    }

    private static string GetHealthStatus(byte percent)
    {
        if (percent > 100) return "Unknown";
        if (percent >= 80) return "Good";
        if (percent >= 50) return "Fair";
        if (percent >= 20) return "Low";
        return "Critical";
    }
}
