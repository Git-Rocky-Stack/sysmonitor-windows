using Microsoft.UI.Xaml;
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

    private async void OpenPdf_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenPdfCommand.ExecuteAsync(null);
    }

    private async void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);
    }

    private async void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PreviousPageCommand.ExecuteAsync(null);
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.NextPageCommand.ExecuteAsync(null);
    }

    private async void RotateLeft_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RotateCurrentPageCommand.ExecuteAsync(-90);
    }

    private async void RotateRight_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RotateCurrentPageCommand.ExecuteAsync(90);
    }

    private async void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.DeleteCurrentPageCommand.ExecuteAsync(null);
    }
}
