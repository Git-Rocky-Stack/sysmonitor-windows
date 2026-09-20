using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class DuplicateFinderPage : Page
{
    public DuplicateFinderViewModel ViewModel { get; }

    public DuplicateFinderPage()
    {
        ViewModel = App.GetService<DuplicateFinderViewModel>();
        InitializeComponent();

        // Nothing is deleted without being asked for, in the page where the person can see what is selected.
        ViewModel.ConfirmDeletion = AskBeforeDeletingAsync;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    /// <summary>Asks before duplicates are removed, saying how many, how much, and where they go.</summary>
    private async Task<bool> AskBeforeDeletingAsync(int fileCount, long bytes)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Move duplicates to the Recycle Bin?",
            Content = $"{fileCount} file(s), {FormatSize(bytes)}. The oldest copy in each group is kept.\n\n" +
                      "They go to the Recycle Bin, so they can be restored from there.",
            PrimaryButtonText = "Move to Recycle Bin",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:F2} GB",
        >= 1_000_000 => $"{bytes / 1_000_000.0:F2} MB",
        >= 1_000 => $"{bytes / 1_000.0:F2} KB",
        _ => $"{bytes} B",
    };
}
