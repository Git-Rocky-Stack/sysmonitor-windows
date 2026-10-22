using SysMonitor.App.Controls.Instruments;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class StartupPage : Page
{
    public StartupViewModel ViewModel { get; }

    public StartupPage()
    {
        ViewModel = App.GetService<StartupViewModel>();
        InitializeComponent();

        // Armed actions ask first, over this page (see Confirmation).
        ViewModel.Confirm = (title, message, confirmText) => ConsoleDialog.ConfirmAsync(this, title, message, confirmText);
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.InitializeAsync();
    }
}
