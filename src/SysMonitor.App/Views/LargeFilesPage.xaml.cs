using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class LargeFilesPage : Page
{
    public LargeFilesViewModel ViewModel { get; }

    public LargeFilesPage()
    {
        ViewModel = App.GetService<LargeFilesViewModel>();
        InitializeComponent();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }
}
