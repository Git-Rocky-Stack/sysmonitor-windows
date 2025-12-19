using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryViewModel ViewModel { get; }

    private CartesianChart? _cpuChart;
    private CartesianChart? _memoryChart;
    private CartesianChart? _temperatureChart;

    public HistoryPage()
    {
        ViewModel = App.GetService<HistoryViewModel>();
        InitializeComponent();

        // Create charts programmatically
        CreateCharts();

        Loaded += async (s, e) => await ViewModel.InitializeAsync();
    }

    private void CreateCharts()
    {
        // CPU Chart
        _cpuChart = new CartesianChart
        {
            Height = 180,
            ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.X,
            TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Top
        };
        _cpuChart.SetBinding(CartesianChart.SeriesProperty,
            new Microsoft.UI.Xaml.Data.Binding { Source = ViewModel, Path = new Microsoft.UI.Xaml.PropertyPath("CpuSeries"), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        _cpuChart.XAxes = ViewModel.TimeAxes;
        _cpuChart.YAxes = ViewModel.PercentAxes;
        CpuChartContainer.Children.Add(_cpuChart);

        // Memory Chart
        _memoryChart = new CartesianChart
        {
            Height = 180,
            ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.X,
            TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Top
        };
        _memoryChart.SetBinding(CartesianChart.SeriesProperty,
            new Microsoft.UI.Xaml.Data.Binding { Source = ViewModel, Path = new Microsoft.UI.Xaml.PropertyPath("MemorySeries"), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        _memoryChart.XAxes = ViewModel.TimeAxes;
        _memoryChart.YAxes = ViewModel.PercentAxes;
        MemoryChartContainer.Children.Add(_memoryChart);

        // Temperature Chart
        _temperatureChart = new CartesianChart
        {
            Height = 180,
            ZoomMode = LiveChartsCore.Measure.ZoomAndPanMode.X,
            TooltipPosition = LiveChartsCore.Measure.TooltipPosition.Top,
            LegendPosition = LiveChartsCore.Measure.LegendPosition.Right
        };
        _temperatureChart.SetBinding(CartesianChart.SeriesProperty,
            new Microsoft.UI.Xaml.Data.Binding { Source = ViewModel, Path = new Microsoft.UI.Xaml.PropertyPath("TemperatureSeries"), Mode = Microsoft.UI.Xaml.Data.BindingMode.OneWay });
        _temperatureChart.XAxes = ViewModel.TimeAxes;
        _temperatureChart.YAxes = ViewModel.TempAxes;
        TemperatureChartContainer.Children.Add(_temperatureChart);
    }
}
