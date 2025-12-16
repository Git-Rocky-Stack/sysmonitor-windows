using CommunityToolkit.Mvvm.ComponentModel;
using System.Reflection;

namespace SysMonitor.App.ViewModels;

public partial class UserGuideViewModel : ObservableObject
{
    [ObservableProperty]
    private string _version = "1.0.0";

    [ObservableProperty]
    private string _buildDate = "December 2024";

    public UserGuideViewModel()
    {
        // Get version from assembly
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetName().Version;
        if (version != null)
        {
            Version = $"{version.Major}.{version.Minor}.{version.Build}";
        }

        // Get build date from assembly (linker timestamp or file info)
        try
        {
            var buildDateTime = GetBuildDate(assembly);
            BuildDate = buildDateTime.ToString("MMMM yyyy");
        }
        catch
        {
            BuildDate = "December 2024";
        }
    }

    private static DateTime GetBuildDate(Assembly assembly)
    {
        // Try to get from assembly file info
        var location = assembly.Location;
        if (!string.IsNullOrEmpty(location) && File.Exists(location))
        {
            return File.GetLastWriteTime(location);
        }

        // Fallback to current date
        return DateTime.Now;
    }
}
