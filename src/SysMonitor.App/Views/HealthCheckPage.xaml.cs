using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class HealthCheckPage : Page
{
    public HealthCheckViewModel ViewModel { get; }

    public HealthCheckPage()
    {
        ViewModel = App.GetService<HealthCheckViewModel>();
        InitializeComponent();
    }
}
