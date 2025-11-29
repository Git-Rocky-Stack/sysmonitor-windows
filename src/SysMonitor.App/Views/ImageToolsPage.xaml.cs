using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class ImageToolsPage : Page
{
    public ImageToolsViewModel ViewModel { get; }

    public ImageToolsPage()
    {
        ViewModel = App.GetService<ImageToolsViewModel>();
        InitializeComponent();
    }
}
