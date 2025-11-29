using LibreHardwareMonitor.Hardware;

namespace SysMonitor.Core.Services.Monitors;

public class TemperatureMonitor : ITemperatureMonitor
{
    private Computer? _computer;
    private bool _isInitialized;

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        await Task.Run(() =>
        {
            try
            {
                _computer = new Computer
                {
                    IsCpuEnabled = true,
                    IsGpuEnabled = true,
                    IsMotherboardEnabled = true,
                    IsStorageEnabled = true
                };
                _computer.Open();
                _isInitialized = true;
            }
            catch { }
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
