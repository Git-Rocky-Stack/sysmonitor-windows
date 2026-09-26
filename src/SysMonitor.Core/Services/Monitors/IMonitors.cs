using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Monitors;

public interface ICpuMonitor
{
    Task<CpuInfo> GetCpuInfoAsync();
    Task<double> GetUsagePercentAsync();
    Task<double> GetTemperatureAsync();
    Task<List<double>> GetCoreUsagesAsync();

    /// <summary>
    /// A sampler with a baseline of its own. A consumer that reads usage on its own schedule should hold one
    /// rather than call <see cref="GetUsagePercentAsync"/>, which every other caller shares.
    /// </summary>
    CpuSampler CreateSampler();
}

public interface IMemoryMonitor
{
    Task<MemoryInfo> GetMemoryInfoAsync();
    Task<double> GetUsagePercentAsync();
}

public interface IDiskMonitor
{
    Task<List<DiskInfo>> GetAllDisksAsync();
    Task<DiskInfo?> GetDiskAsync(string driveLetter);
    Task<(double read, double write)> GetDiskSpeedAsync(string driveLetter);
}

public interface IBatteryMonitor
{
    Task<BatteryInfo?> GetBatteryInfoAsync();
    bool HasBattery { get; }
}

public interface INetworkMonitor
{
    Task<NetworkInfo> GetNetworkInfoAsync();
    Task<List<NetworkAdapter>> GetAdaptersAsync();
    Task<(double upload, double download)> GetSpeedAsync();
}

public interface IProcessMonitor
{
    Task<List<ProcessInfo>> GetAllProcessesAsync();
    Task<ProcessInfo?> GetProcessAsync(int processId);
    Task<bool> KillProcessAsync(int processId);
    Task<bool> SetPriorityAsync(int processId, ProcessPriority priority);
}

/// <summary>
/// A frame-rate reading, or the reason there is not one. Frame rate is the one figure in the overlay that
/// most machines cannot produce: the only sensor that reports it belongs to AMD GPUs, so "no reading" is the
/// normal answer and has to be told apart from a reading of zero.
/// </summary>
public readonly record struct FrameRate
{
    private FrameRate(double? framesPerSecond, bool hasSensor)
    {
        FramesPerSecond = framesPerSecond;
        HasSensor = hasSensor;
    }

    /// <summary>Frames per second, or null when there is no reading right now.</summary>
    public double? FramesPerSecond { get; }

    /// <summary>Whether this machine has a sensor that can report frame rate at all.</summary>
    public bool HasSensor { get; }

    /// <summary>Nothing on this machine reports frame rate.</summary>
    public static FrameRate NoSensor => new(null, hasSensor: false);

    /// <summary>The sensor is there, but nothing is drawing fullscreen for it to measure.</summary>
    public static FrameRate Idle => new(null, hasSensor: true);

    /// <summary>A measured frame rate.</summary>
    public static FrameRate Of(double framesPerSecond) => new(framesPerSecond, hasSensor: true);

    /// <summary>
    /// What a frame-rate sensor's current value means. The sensor exists, so this machine can report frame
    /// rate - but it reads -1 while nothing is drawing fullscreen (LibreHardwareMonitorLib 0.9.3,
    /// AmdGpu.cs:123 sets -1 on activation, :237 replaces it once frames are measured). That is not a frame
    /// rate and must never be shown as one; neither is a zero, nor a sensor that has not read yet.
    /// </summary>
    public static FrameRate FromSensor(double? sensorValue) =>
        sensorValue is > 0 ? Of(sensorValue.Value) : Idle;
}

/// <summary>
/// Reads temperatures, fans and power from the hardware.
/// <para>
/// <see cref="IDisposable"/> because opening the hardware loads a kernel driver, and closing it is what
/// gives that driver back. <c>Dispose</c> used to be declared here on its own, without the interface
/// extending <see cref="IDisposable"/> - so nothing in the app could see there was anything to release,
/// and nothing ever called it.
/// </para>
/// </summary>
public interface ITemperatureMonitor : IDisposable
{
    Task InitializeAsync();
    Task<Dictionary<string, double>> GetAllTemperaturesAsync();
    Task<double> GetCpuTemperatureAsync();
    Task<double> GetGpuTemperatureAsync();
    Task<Dictionary<string, double>> GetAllFanSpeedsAsync();
    Task<Dictionary<string, double>> GetAllPowerReadingsAsync();
    Task<Dictionary<string, double>> GetAllLoadSensorsAsync();

    /// <summary>
    /// What the hardware reports about frame rate. See <see cref="FrameRate"/>: on most machines there is
    /// nothing to report, and that is an answer, not a zero.
    /// </summary>
    Task<FrameRate> GetFrameRateAsync();
    Task<double> GetTotalSystemPowerAsync();
    Task<List<string>> GetAllSensorsDiagnosticAsync();
}
