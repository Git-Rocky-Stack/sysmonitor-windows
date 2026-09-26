using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SysMonitor.App.Controls.Instruments;
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
    private Task<bool> AskBeforeDeletingAsync(int fileCount, long bytes) =>
        ConsoleDialog.ConfirmAsync(this, "Move duplicates to the Recycle Bin?",
            $"{fileCount} file(s), {FormatSize(bytes)}. The oldest copy in each group is kept.\n\n" +
            "They go to the Recycle Bin, so they can be restored from there. A file Windows cannot recycle, such " +
            "as one on a network or removable drive or one larger than the Recycle Bin is set to hold, is left " +
            "where it is, and the result says so.",
            "Move to Recycle Bin");

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1_000_000_000 => $"{bytes / 1_000_000_000.0:F2} GB",
        >= 1_000_000 => $"{bytes / 1_000_000.0:F2} MB",
        >= 1_000 => $"{bytes / 1_000.0:F2} KB",
        _ => $"{bytes} B",
    };
}
