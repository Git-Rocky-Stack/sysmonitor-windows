using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class CleanerPage : Page
{
    public CleanerViewModel ViewModel { get; }

    public CleanerPage()
    {
        ViewModel = App.GetService<CleanerViewModel>();
        InitializeComponent();
    }
}
