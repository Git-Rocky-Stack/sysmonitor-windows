using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using LibreHardwareMonitor.Hardware;

namespace SysMonitor.Core.Services.Monitors;

public class TemperatureMonitor : ITemperatureMonitor
{
    private readonly ILogger _logger;

    public TemperatureMonitor(ILogger<TemperatureMonitor>? logger = null)
    {
        _logger = logger ?? NullLogger<TemperatureMonitor>.Instance;
    }

    /// <summary>
    /// Guards every touch of <see cref="_computer"/>.
    /// <para>
    /// One instance is shared by the 5-second dashboard timer, the 30-second history timer, the alert
    /// check and the overlay loop. <c>IHardware.Update()</c> walks and rewrites the sensor tree in place;
    /// two of those callers doing it at once is a read of a half-rewritten tree, and the symptom is a
    /// temperature that is wrong or an exception from inside the driver.
    /// </para>
    /// </summary>
    private readonly object _hardwareGate = new();

    private Computer? _computer;
    private bool _isInitialized;
    private bool _initializationFailed;

    public async Task InitializeAsync()
    {
        await Task.Run(() =>
        {
            lock (_hardwareGate)
            {
                // Checked inside the gate: two callers that both passed an unguarded check would each
                // open a Computer, and the second would replace the first - leaking its driver handle.
                if (_isInitialized || _initializationFailed) return;

                Open();
            }
        });
    }

    /// <summary>Opens the hardware. The caller holds <see cref="_hardwareGate"/>.</summary>
    private void Open()
    {
        try
        {
            var computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMotherboardEnabled = true,
                IsStorageEnabled = true,
                IsControllerEnabled = true,  // Enable fan controller sensors
                IsPsuEnabled = true,         // Enable PSU sensors
                IsNetworkEnabled = false,
                IsBatteryEnabled = true
            };
            computer.Open();
            _computer = computer;
            _isInitialized = true;
        }
        catch (Exception)
        {
            // LibreHardwareMonitor can throw NullReferenceException from Ring0.Open()
            // when running without admin privileges or when the driver fails to load.
            // Mark as failed to prevent repeated initialization attempts.
            _initializationFailed = true;
            _computer = null;
        }
    }

    public async Task<Dictionary<string, double>> GetAllTemperaturesAsync()
    {
        return await Task.Run(() =>
        {
            lock (_hardwareGate)
            {
            var temps = new Dictionary<string, double>();
            if (_computer == null) return temps;

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Temperature && sensor.Value.HasValue)
                        {
                            temps[$"{hardware.Name} - {sensor.Name}"] = sensor.Value.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetAllTemperaturesAsync failed");
            }
            return temps;
            }
        });
    }

    public async Task<double> GetCpuTemperatureAsync()
    {
        var temps = await GetAllTemperaturesAsync();
        var cpuTemp = temps.FirstOrDefault(t =>
            t.Key.Contains("CPU", StringComparison.OrdinalIgnoreCase) &&
            t.Key.Contains("Package", StringComparison.OrdinalIgnoreCase));

        if (cpuTemp.Key != null) return cpuTemp.Value;

        cpuTemp = temps.FirstOrDefault(t =>
            t.Key.Contains("CPU", StringComparison.OrdinalIgnoreCase));

        return cpuTemp.Key != null ? cpuTemp.Value : 0;
    }

    public async Task<double> GetGpuTemperatureAsync()
    {
        var temps = await GetAllTemperaturesAsync();
        var gpuTemp = temps.FirstOrDefault(t =>
            t.Key.Contains("GPU", StringComparison.OrdinalIgnoreCase));
        return gpuTemp.Key != null ? gpuTemp.Value : 0;
    }

    public async Task<Dictionary<string, double>> GetAllFanSpeedsAsync()
    {
        return await Task.Run(() =>
        {
            lock (_hardwareGate)
            {
            var fans = new Dictionary<string, double>();
            if (_computer == null) return fans;

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        // Check for Fan (RPM) and Control (fan speed %) sensors
                        if ((sensor.SensorType == SensorType.Fan ||
                             (sensor.SensorType == SensorType.Control &&
                              sensor.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase))) &&
                            sensor.Value.HasValue && sensor.Value.Value > 0)
                        {
                            var suffix = sensor.SensorType == SensorType.Control ? " %" : "";
                            fans[$"{hardware.Name} - {sensor.Name}{suffix}"] = sensor.Value.Value;
                        }
                    }
                    // Check sub-hardware (like motherboard chips - IT87xx, NCT67xx, etc.)
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                        foreach (var sensor in subHardware.Sensors)
                        {
                            if ((sensor.SensorType == SensorType.Fan ||
                                 (sensor.SensorType == SensorType.Control &&
                                  sensor.Name.Contains("Fan", StringComparison.OrdinalIgnoreCase))) &&
                                sensor.Value.HasValue && sensor.Value.Value > 0)
                            {
                                var suffix = sensor.SensorType == SensorType.Control ? " %" : "";
                                fans[$"{subHardware.Name} - {sensor.Name}{suffix}"] = sensor.Value.Value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetAllFanSpeedsAsync failed");
            }
            return fans;
            }
        });
    }

    public async Task<Dictionary<string, double>> GetAllPowerReadingsAsync()
    {
        return await Task.Run(() =>
        {
            lock (_hardwareGate)
            {
            var power = new Dictionary<string, double>();
            if (_computer == null) return power;

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType == SensorType.Power && sensor.Value.HasValue)
                        {
                            power[$"{hardware.Name} - {sensor.Name}"] = sensor.Value.Value;
                        }
                    }
                    // Check sub-hardware
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                        foreach (var sensor in subHardware.Sensors)
                        {
                            if (sensor.SensorType == SensorType.Power && sensor.Value.HasValue)
                            {
                                power[$"{subHardware.Name} - {sensor.Name}"] = sensor.Value.Value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetAllPowerReadingsAsync failed");
            }
            return power;
            }
        });
    }

    public async Task<Dictionary<string, double>> GetAllLoadSensorsAsync()
    {
        return await Task.Run(() =>
        {
            lock (_hardwareGate)
            {
            var loads = new Dictionary<string, double>();
            if (_computer == null) return loads;

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        // Usage and throughput. Frame rate is not among these - see GetFrameRateAsync.
                        if ((sensor.SensorType == SensorType.Load ||
                             sensor.SensorType == SensorType.SmallData ||
                             sensor.SensorType == SensorType.Throughput) &&
                            sensor.Value.HasValue)
                        {
                            loads[$"{hardware.Name} - {sensor.Name}"] = sensor.Value.Value;
                        }
                    }
                    // Check sub-hardware
                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                        foreach (var sensor in subHardware.Sensors)
                        {
                            if ((sensor.SensorType == SensorType.Load ||
                                 sensor.SensorType == SensorType.SmallData ||
                                 sensor.SensorType == SensorType.Throughput) &&
                                sensor.Value.HasValue)
                            {
                                loads[$"{subHardware.Name} - {sensor.Name}"] = sensor.Value.Value;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetAllLoadSensorsAsync failed");
            }
            return loads;
            }
        });
    }

    /// <summary>
    /// The name LibreHardwareMonitor gives its one frame-rate sensor, on AMD GPUs whose driver exposes the
    /// ADL2 FrameMetrics API (LibreHardwareMonitorLib 0.9.3, AmdGpu.cs:79). It is a Factor sensor, not a Load
    /// one, and it reads -1 until a fullscreen application reports a frame (AmdGpu.cs:123, :237).
    /// </summary>
    private const string FrameRateSensorName = "Fullscreen FPS";

    public async Task<FrameRate> GetFrameRateAsync()
    {
        return await Task.Run(() =>
        {
            lock (_hardwareGate)
            {
            if (_computer == null) return FrameRate.NoSensor;

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.SensorType != SensorType.Factor ||
                            !sensor.Name.Contains(FrameRateSensorName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        return FrameRate.FromSensor(sensor.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetFrameRateAsync failed");
            }

            return FrameRate.NoSensor;
            }
        });
    }

    public async Task<double> GetTotalSystemPowerAsync()
    {
        var powerReadings = await GetAllPowerReadingsAsync();

        // Look for CPU Package power first (most accurate for CPU)
        var cpuPower = powerReadings
            .Where(p => p.Key.Contains("CPU", StringComparison.OrdinalIgnoreCase) &&
                       p.Key.Contains("Package", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value)
            .FirstOrDefault();

        // Look for GPU power
        var gpuPower = powerReadings
            .Where(p => p.Key.Contains("GPU", StringComparison.OrdinalIgnoreCase))
            .Select(p => p.Value)
            .FirstOrDefault();

        // Return combined CPU + GPU power as approximation
        return cpuPower + gpuPower;
    }

    public async Task<List<string>> GetAllSensorsDiagnosticAsync()
    {
        return await Task.Run(() =>
        {
            lock (_hardwareGate)
            {
            var sensors = new List<string>();
            if (_computer == null)
            {
                sensors.Add("ERROR: Computer not initialized");
                return sensors;
            }

            try
            {
                foreach (var hardware in _computer.Hardware)
                {
                    hardware.Update();
                    sensors.Add($"[HARDWARE] {hardware.HardwareType}: {hardware.Name}");

                    foreach (var sensor in hardware.Sensors)
                    {
                        if (sensor.Value.HasValue)
                        {
                            sensors.Add($"  [{sensor.SensorType}] {sensor.Name}: {sensor.Value.Value:F1}");
                        }
                    }

                    foreach (var subHardware in hardware.SubHardware)
                    {
                        subHardware.Update();
                        sensors.Add($"  [SUB-HARDWARE] {subHardware.HardwareType}: {subHardware.Name}");

                        foreach (var sensor in subHardware.Sensors)
                        {
                            if (sensor.Value.HasValue)
                            {
                                sensors.Add($"    [{sensor.SensorType}] {sensor.Name}: {sensor.Value.Value:F1}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sensors.Add($"ERROR: {ex.Message}");
            }

            return sensors;
            }
        });
    }

    /// <summary>
    /// Closes the hardware, which releases the kernel driver LibreHardwareMonitor loaded to read the
    /// sensors. Nothing called this: <c>Dispose</c> was declared on the interface, but the interface did
    /// not extend <see cref="IDisposable"/>, so the host had no way to know there was anything to release.
    /// </summary>
    public void Dispose()
    {
        lock (_hardwareGate)
        {
            _computer?.Close();
            _computer = null;
            _isInitialized = false;
        }

        GC.SuppressFinalize(this);
    }
}
