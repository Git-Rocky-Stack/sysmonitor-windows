using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class DonationPage : Page
{
    public DonationViewModel ViewModel { get; }

    public DonationPage()
    {
        ViewModel = App.GetService<DonationViewModel>();
        InitializeComponent();
    }
}
