using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;
using SysMonitor.Core.Services.Monitors;

namespace SysMonitor.App.Views;

public sealed partial class GameModePage : Page
{
    public GameModeViewModel ViewModel { get; }

    private readonly ITemperatureMonitor _temperatureMonitor;
    private readonly DispatcherTimer _updateTimer;

    public GameModePage()
    {
        ViewModel = App.GetService<GameModeViewModel>();
        _temperatureMonitor = App.GetService<ITemperatureMonitor>();

        InitializeComponent();

        // Set up timer for real-time monitoring updates
        _updateTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(1000)
        };
        _updateTimer.Tick += UpdateTimer_Tick;

        Loaded += GameModePage_Loaded;
        Unloaded += GameModePage_Unloaded;
    }

    private async void GameModePage_Loaded(object sender, RoutedEventArgs e)
    {
        // Ensure temperature monitor is initialized
        await _temperatureMonitor.InitializeAsync();
        _updateTimer.Start();
        await UpdateMonitoringWidgetsAsync();
    }

    private void GameModePage_Unloaded(object sender, RoutedEventArgs e)
    {
        _updateTimer.Stop();
    }

    private async void UpdateTimer_Tick(object? sender, object e)
    {
        await UpdateMonitoringWidgetsAsync();
    }

    private async Task UpdateMonitoringWidgetsAsync()
    {
        try
        {
            // Get fan speeds - look for any fan sensors
            var fanSpeeds = await _temperatureMonitor.GetAllFanSpeedsAsync();
            var fanList = fanSpeeds.OrderByDescending(f => f.Value).ToList();

            double fan1Speed = fanList.Count > 0 ? fanList[0].Value : 0;
            double fan2Speed = fanList.Count > 1 ? fanList[1].Value : 0;

            // Try to identify CPU fan specifically
            var cpuFan = fanSpeeds.FirstOrDefault(f =>
                f.Key.Contains("CPU", StringComparison.OrdinalIgnoreCase) ||
                f.Key.Contains("#1", StringComparison.OrdinalIgnoreCase) ||
                f.Key.Contains("Fan 1", StringComparison.OrdinalIgnoreCase));

            if (cpuFan.Value > 0)
            {
                fan1Speed = cpuFan.Value;
                // Get second highest that's not CPU fan
                fan2Speed = fanSpeeds.Where(f => f.Key != cpuFan.Key)
                    .OrderByDescending(f => f.Value)
                    .Select(f => f.Value)
                    .FirstOrDefault();
            }

            CpuFanSpeedText.Text = fan1Speed > 0 ? $"{fan1Speed:F0} RPM" : "N/A";
            SysFanSpeedText.Text = fan2Speed > 0 ? $"{fan2Speed:F0} RPM" : "N/A";

            // Get power readings
            var powerReadings = await _temperatureMonitor.GetAllPowerReadingsAsync();
            double cpuPower = 0;
            double gpuPower = 0;

            foreach (var power in powerReadings)
            {
                var key = power.Key.ToUpperInvariant();
                if (key.Contains("CPU"))
                {
                    // Prefer Package power, but take any CPU power
                    if (key.Contains("PACKAGE") || cpuPower == 0)
                    {
                        cpuPower = Math.Max(cpuPower, power.Value);
                    }
                }
                else if (key.Contains("GPU") || key.Contains("GRAPHICS"))
                {
                    // Take highest GPU power reading
                    gpuPower = Math.Max(gpuPower, power.Value);
                }
            }

            // If no specific readings, try to get any power readings
            if (cpuPower == 0 && gpuPower == 0 && powerReadings.Count > 0)
            {
                var sortedPower = powerReadings.OrderByDescending(p => p.Value).ToList();
                if (sortedPower.Count > 0) cpuPower = sortedPower[0].Value;
                if (sortedPower.Count > 1) gpuPower = sortedPower[1].Value;
            }

            CpuPowerText.Text = cpuPower > 0 ? $"{cpuPower:F0}W" : "N/A";
            GpuPowerText.Text = gpuPower > 0 ? $"{gpuPower:F0}W" : "N/A";
            TotalPowerText.Text = (cpuPower + gpuPower) > 0 ? $"{cpuPower + gpuPower:F0}W" : "N/A";
        }
        catch
        {
            // Silently handle errors in monitoring
        }
    }

    private void RamCacheToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleSwitch toggle)
        {
            // Only trigger if the state is different from current
            if (toggle.IsOn != ViewModel.RamCacheEnabled)
            {
                ViewModel.ToggleRamCacheCommand.Execute(null);
            }
        }
    }

    private async void ViewSensors_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var sensors = await _temperatureMonitor.GetAllSensorsDiagnosticAsync();
            var content = string.Join("\n", sensors);

            var dialog = new ContentDialog
            {
                Title = "All Hardware Sensors",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = content,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        FontSize = 11,
                        TextWrapping = TextWrapping.NoWrap
                    },
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 400
                },
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            var dialog = new ContentDialog
            {
                Title = "Error",
                Content = $"Failed to get sensors: {ex.Message}",
                CloseButtonText = "Close",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
    }
}
