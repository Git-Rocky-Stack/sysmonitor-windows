using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class ScheduledCleaningPage : Page
{
    public ScheduledCleaningViewModel ViewModel { get; }

    public ScheduledCleaningPage()
    {
        ViewModel = App.GetService<ScheduledCleaningViewModel>();
        InitializeComponent();
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        await ViewModel.InitializeAsync();
    }
}
