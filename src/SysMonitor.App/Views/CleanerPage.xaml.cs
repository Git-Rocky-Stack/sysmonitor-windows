using SysMonitor.App.Controls.Instruments;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class CleanerPage : Page
{
    public CleanerViewModel ViewModel { get; }

    public CleanerPage()
    {
        ViewModel = App.GetService<CleanerViewModel>();
        InitializeComponent();

        // Armed actions ask first, over this page (see Confirmation).
        ViewModel.Confirm = (title, message, confirmText) => ConsoleDialog.ConfirmAsync(this, title, message, confirmText);
    }
}
