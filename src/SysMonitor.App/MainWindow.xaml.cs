using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.Views;

namespace SysMonitor.App;

public sealed partial class MainWindow : Window
{
    private readonly Dictionary<string, Type> _pageMap = new()
    {
        { "Dashboard", typeof(DashboardPage) },
        { "Processes", typeof(ProcessesPage) },
        { "Cleaner", typeof(CleanerPage) },
        { "Startup", typeof(StartupPage) },
        { "DiskAnalyzer", typeof(PlaceholderPage) },
        { "Network", typeof(PlaceholderPage) }
    };

    public MainWindow()
    {
        InitializeComponent();
        Title = "SysMonitor - Windows System Monitor & Optimizer";

        // Navigate to dashboard on startup
        ContentFrame.Navigate(typeof(DashboardPage));
        NavView.SelectedItem = NavView.MenuItems[0];
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (args.SelectedItemContainer is NavigationViewItem item)
        {
            var tag = item.Tag?.ToString();
            if (tag != null && _pageMap.TryGetValue(tag, out var pageType))
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }
}
