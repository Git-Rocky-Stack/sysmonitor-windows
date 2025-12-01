using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class PdfEditorPage : Page
{
    public PdfEditorViewModel ViewModel { get; }

    public PdfEditorPage()
    {
        ViewModel = App.GetService<PdfEditorViewModel>();
        InitializeComponent();
    }
}
