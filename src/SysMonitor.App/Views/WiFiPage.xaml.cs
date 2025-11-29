using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class WiFiPage : Page
{
    public WiFiViewModel ViewModel { get; }

    public WiFiPage()
    {
        ViewModel = App.GetService<WiFiViewModel>();
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }
}
