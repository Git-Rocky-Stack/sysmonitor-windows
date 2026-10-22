using SysMonitor.App.Controls.Instruments;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class HealthCheckPage : Page
{
    public HealthCheckViewModel ViewModel { get; }

    public HealthCheckPage()
    {
        ViewModel = App.GetService<HealthCheckViewModel>();
        InitializeComponent();

        // Armed actions ask first, over this page (see Confirmation).
        ViewModel.Confirm = (title, message, confirmText) => ConsoleDialog.ConfirmAsync(this, title, message, confirmText);
    }
}
