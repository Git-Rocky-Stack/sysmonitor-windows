using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class FileToolsPage : Page
{
    public FileToolsViewModel ViewModel { get; }

    public FileToolsPage()
    {
        ViewModel = App.GetService<FileToolsViewModel>();
        InitializeComponent();
    }
}
