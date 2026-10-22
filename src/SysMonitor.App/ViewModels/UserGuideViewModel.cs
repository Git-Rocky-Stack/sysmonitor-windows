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

        BuildDate = BuildDateOf(assembly)?.ToString("MMMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
                    ?? "an unrecorded date";
    }

    /// <summary>
    /// The date the build stamped into the assembly (SysMonitor.App.csproj, AssemblyMetadata "BuildDate"). The
    /// file's modified time, read before, is the day the installer unpacked it - or the Store's packaging date -
    /// and the fallback named a December 2024 build that never was.
    /// </summary>
    internal static DateTime? BuildDateOf(Assembly assembly)
    {
        var stamped = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "BuildDate")?.Value;

        return DateTime.TryParseExact(stamped, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal, out var date)
            ? date
            : null;
    }
}
