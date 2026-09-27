namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// The resource keys WinUI itself provides, as scripts/list-winui-keys.py read them out of the Windows App SDK's
/// own theme dictionary (winui-resource-keys.txt, beside this file).
/// <para>
/// XamlControlsResources, merged first in App.xaml, brings every one of them into the application. So a reference
/// to one of these keys resolves, and a resource the app defines under one of these names replaces WinUI's for
/// every control that reads it. Neither is anything the XAML compiler checks.
/// </para>
/// </summary>
internal static class WinUIResources
{
    private const string ListFile = "tests/SysMonitor.Tests/TestSupport/winui-resource-keys.txt";
    private const string PackageLine = "# Microsoft.WindowsAppSDK ";

    private static readonly Lazy<(HashSet<string> Keys, string Version)> List = new(Read);

    /// <summary>Every key WinUI defines or supplies.</summary>
    public static IReadOnlySet<string> Keys => List.Value.Keys;

    /// <summary>The Windows App SDK version the list was read from.</summary>
    public static string PackageVersion => List.Value.Version;

    private static (HashSet<string> Keys, string Version) Read()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoSource.Root, ListFile.Replace('/', Path.DirectorySeparatorChar)));
        var version = lines.FirstOrDefault(line => line.StartsWith(PackageLine, StringComparison.Ordinal))?[PackageLine.Length..].Trim()
            ?? throw new InvalidDataException($"{ListFile} does not say which Windows App SDK it was read from.");

        var keys = lines.Where(line => line.Length > 0 && !line.StartsWith('#')).ToHashSet(StringComparer.Ordinal);
        return (keys, version);
    }
}
