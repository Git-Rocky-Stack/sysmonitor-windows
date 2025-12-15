using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class BrowserPrivacyPage : Page
{
    public BrowserPrivacyViewModel ViewModel { get; }

    public BrowserPrivacyPage()
    {
        ViewModel = App.GetService<BrowserPrivacyViewModel>();
        InitializeComponent();
    }
}
