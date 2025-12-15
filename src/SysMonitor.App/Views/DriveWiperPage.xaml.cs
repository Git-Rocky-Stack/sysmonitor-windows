using Microsoft.UI.Xaml.Controls;
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
}
