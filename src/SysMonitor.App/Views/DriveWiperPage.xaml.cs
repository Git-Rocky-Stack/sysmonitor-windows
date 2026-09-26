using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SysMonitor.App.Controls.Instruments;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class DriveWiperPage : Page
{
    public DriveWiperViewModel ViewModel { get; }

    public DriveWiperPage()
    {
        ViewModel = App.GetService<DriveWiperViewModel>();
        InitializeComponent();

        // Nothing is overwritten without being asked for, in the page where the person can see what is listed.
        ViewModel.ConfirmWipe = AskBeforeWipingAsync;
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

    /// <summary>
    /// Asks before a wipe starts, saying how much is listed and that none of it can be brought back. Cancel is
    /// the default, so Enter or Escape leaves every file where it is.
    /// </summary>
    private Task<bool> AskBeforeWipingAsync(WipeConfirmation wipe)
    {
        var items = Count(wipe.Files, "file") + (wipe.Folders > 0 && wipe.Files > 0 ? " and " : "") +
                    Count(wipe.Folders, "folder");
        var message = $"{items}, {wipe.TotalSize} in total. Everything listed is overwritten and then deleted. " +
                      "None of it goes to the Recycle Bin, and this cannot be undone.";

        if (wipe.MediaWarning is not null)
            message += "\n\n" + wipe.MediaWarning;

        return ConsoleDialog.ConfirmAsync(this, $"Wipe {Count(wipe.Files + wipe.Folders, "item")}?", message,
            "Wipe");
    }

    /// <summary>"1 file", "3 folders"; nothing at all for none.</summary>
    private static string Count(int count, string noun) => count switch
    {
        0 => "",
        1 => $"1 {noun}",
        _ => $"{count} {noun}s",
    };
}
