using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class DriveWiperPage : Page
{
    public DriveWiperViewModel ViewModel { get; }

    public DriveWiperPage()
    {
        ViewModel = App.GetService<DriveWiperViewModel>();
        InitializeComponent();
    }

    /// <summary>
    /// Ends what this page started. The view model is built for one visit and holds the work it kicked off;
    /// leaving without this left a registry scan, a wipe or a backup running against a page that was gone.
    /// </summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

}
