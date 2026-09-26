using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SysMonitor.App.Controls.Instruments;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class LargeFilesPage : Page
{
    public LargeFilesViewModel ViewModel { get; }

    public LargeFilesPage()
    {
        ViewModel = App.GetService<LargeFilesViewModel>();
        InitializeComponent();

        // Nothing is removed without being asked for, in the page where the person can see what is selected.
        ViewModel.ConfirmDeletion = AskBeforeDeletingAsync;
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }

    /// <summary>
    /// Asks before the selected files are removed, saying how many, how much, and where they go - including
    /// what happens to a file the Recycle Bin cannot take. Cancel is the default.
    /// </summary>
    private Task<bool> AskBeforeDeletingAsync(int fileCount, string totalSize)
    {
        var files = fileCount == 1 ? "1 file" : $"{fileCount} files";
        var message = $"{files}, {totalSize} in total. They go to the Recycle Bin, where they can be restored. " +
                      "A file Windows cannot recycle, such as one on a network or removable drive or one larger " +
                      "than the Recycle Bin is set to hold, is left where it is, and the result says so.";

        return ConsoleDialog.ConfirmAsync(this, $"Move {files} to the Recycle Bin?", message, "Move to Recycle Bin");
    }
}
