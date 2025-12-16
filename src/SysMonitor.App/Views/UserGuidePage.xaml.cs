using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class UserGuidePage : Page
{
    public UserGuideViewModel ViewModel { get; }

    public UserGuidePage()
    {
        ViewModel = App.GetService<UserGuideViewModel>();
        InitializeComponent();
    }
}
