using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
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

    /// <summary>
    /// Ends what this page started. The view model is built for one visit and holds the work it kicked off;
    /// leaving without this left a registry scan, a wipe or a backup running against a page that was gone.
    /// </summary>
    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }


    private async void RestoreBackup_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.HasBackup)
            return;

        var dialog = new ContentDialog
        {
            Title = "Restore Registry Backup",
            Content = $"Import the backup created at {System.IO.File.GetLastWriteTime(ViewModel.LastBackupPath):g}?\n\n" +
                      "This puts back the registry keys and values that the last cleaning changed. " +
                      "Windows asks for administrator permission if the backup contains system-wide keys.",
            PrimaryButtonText = "Restore",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = this.XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RestoreLastBackupAsync();
        }
    }
}
