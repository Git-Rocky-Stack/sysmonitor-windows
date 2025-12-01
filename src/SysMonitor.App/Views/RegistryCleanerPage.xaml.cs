using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class RegistryCleanerPage : Page
{
    public RegistryCleanerViewModel ViewModel { get; }

    public RegistryCleanerPage()
    {
        ViewModel = App.GetService<RegistryCleanerViewModel>();
        InitializeComponent();
    }
}
