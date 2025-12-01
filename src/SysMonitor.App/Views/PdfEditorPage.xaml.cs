using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SysMonitor.App.Views;

public sealed partial class PdfEditorPage : Page
{
    public PdfEditorPage()
    {
        InitializeComponent();
    }

    private void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "PDF Editor functionality coming soon...";
    }
}
