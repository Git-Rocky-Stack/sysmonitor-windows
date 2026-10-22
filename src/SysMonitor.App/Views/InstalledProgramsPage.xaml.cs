using SysMonitor.App.Controls.Instruments;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class InstalledProgramsPage : Page
{
    public InstalledProgramsViewModel ViewModel { get; }

    public InstalledProgramsPage()
    {
        ViewModel = App.GetService<InstalledProgramsViewModel>();
        InitializeComponent();

        // Armed actions ask first, over this page (see Confirmation).
        ViewModel.Confirm = (title, message, confirmText) => ConsoleDialog.ConfirmAsync(this, title, message, confirmText);
        DataContext = ViewModel;

        // Auto-load programs when page loads
        Loaded += async (s, e) =>
        {
            if (ViewModel.Programs.Count == 0)
            {
                await ViewModel.LoadProgramsCommand.ExecuteAsync(null);
            }
        };
    }
}
