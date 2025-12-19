using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Serilog;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class PerformancePage : Page
{
    public PerformanceViewModel ViewModel { get; }

    public PerformancePage()
    {
        Log.Information("PerformancePage: Constructor started");
        try
        {
            Log.Information("PerformancePage: Getting ViewModel from DI");
            ViewModel = App.GetService<PerformanceViewModel>();
            Log.Information("PerformancePage: ViewModel obtained, calling InitializeComponent");
            InitializeComponent();
            Log.Information("PerformancePage: InitializeComponent completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PerformancePage: Constructor failed");
            throw;
        }
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        Log.Information("PerformancePage: OnNavigatedTo started");
        base.OnNavigatedTo(e);
        try
        {
            await ViewModel.InitializeAsync();
            Log.Information("PerformancePage: InitializeAsync completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PerformancePage: OnNavigatedTo failed");
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Log.Information("PerformancePage: OnNavigatedFrom started");
        base.OnNavigatedFrom(e);
        try
        {
            ViewModel.Dispose();
            Log.Information("PerformancePage: Dispose completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "PerformancePage: OnNavigatedFrom failed");
        }
    }
}