using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;

namespace SysMonitor.App.Views;

public sealed partial class GameModePage : Page
{
    public GameModeViewModel ViewModel { get; }

    public GameModePage()
    {
        ViewModel = App.GetService<GameModeViewModel>();
        InitializeComponent();
    }
}
