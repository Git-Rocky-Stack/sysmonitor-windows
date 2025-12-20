using LibreHardwareMonitor.Hardware;

namespace SysMonitor.Core.Services.Monitors;

public class TemperatureMonitor : ITemperatureMonitor
{
    private Computer? _computer;
    private bool _isInitialized;
    private bool _initializationFailed;

    public async Task InitializeAsync()
    {
        if (_isInitialized || _initializationFailed) return;
        await Task.Run(() =>
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
        });
    }

    public async Task<Dictionary<string, double>> GetAllTemperaturesAsync()
    {
        return await Task.Run(() =>
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
            catch { }
            return temps;
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
            catch { }
            return fans;
        });
    }

    public async Task<Dictionary<string, double>> GetAllPowerReadingsAsync()
    {
        return await Task.Run(() =>
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
            catch { }
            return power;
        });
    }

    public async Task<Dictionary<string, double>> GetAllLoadSensorsAsync()
    {
        return await Task.Run(() =>
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
                        // Get Load sensors (CPU/GPU usage) and SmallData (includes FPS/frametime)
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
            catch { }
            return loads;
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
        });
    }

    public void Dispose()
    {
        _computer?.Close();
        _computer = null;
        _isInitialized = false;
    }
}
