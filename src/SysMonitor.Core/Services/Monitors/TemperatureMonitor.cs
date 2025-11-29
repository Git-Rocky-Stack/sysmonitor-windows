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
                    IsStorageEnabled = true
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

    public void Dispose()
    {
        _computer?.Close();
        _computer = null;
        _isInitialized = false;
    }
}
