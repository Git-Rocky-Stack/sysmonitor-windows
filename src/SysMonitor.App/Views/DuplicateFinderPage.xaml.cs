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
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        ViewModel.Dispose();
    }
}
