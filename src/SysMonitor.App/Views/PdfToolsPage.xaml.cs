using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class PdfToolsPage : Page
{
    public PdfToolsViewModel ViewModel { get; }

    public PdfToolsPage()
    {
        ViewModel = App.GetService<PdfToolsViewModel>();
        InitializeComponent();
    }
}
