using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class NetworkMapperPage : Page
{
    public NetworkMapperViewModel ViewModel { get; }

    public NetworkMapperPage()
    {
        ViewModel = App.GetService<NetworkMapperViewModel>();
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
