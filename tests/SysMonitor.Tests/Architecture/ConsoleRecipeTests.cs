using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// The Command Console restyle replaces the first-generation look page by page: colours become theme tokens, the
/// hand-copied chrome bezels around buttons become caps, spinners become busy panels, literal font names become
/// the console faces, and every dialog comes from one helper that gives it the right style, theme and window.
/// <para>
/// Each rule below names the files that still break it. The lists are a ratchet: a file leaves its list in the
/// commit that cleans it, and the test fails both ways - a file that breaks a rule without being on its list, and
/// a file on a list that no longer breaks it. So the old look can only shrink, and the lists say exactly how much
/// of it is left.
/// </para>
/// </summary>
public class ConsoleRecipeTests
{
    private const string AppFolder = "src/SysMonitor.App";
    private const string CoreFolder = "src/SysMonitor.Core";

    private static readonly Regex XamlColour = new(@"=\s*""#[0-9A-Fa-f]{3,8}""", RegexOptions.Compiled);
    private static readonly Regex CodeColour = new(@"""#[0-9A-Fa-f]{6,8}""", RegexOptions.Compiled);
    private static readonly Regex FontFamilyLiteral = new(@"FontFamily=""(?!\{)[^""]*""", RegexOptions.Compiled);

    [Fact]
    public void ColourLiteralsInXamlOnlyRemainWhereTheyAreListed() =>
        Ratchet(Xaml(), source => XamlColour.IsMatch(source), XamlColourLiterals,
            "a colour written into XAML ignores the theme; it belongs in the token dictionaries");

    [Fact]
    public void ColourStringsInCodeOnlyRemainWhereTheyAreListed() =>
        Ratchet(Code(AppFolder).Concat(Code(CoreFolder)), source => CodeColour.IsMatch(source), CodeColourLiterals,
            "a status carries a word and a lamp state, and the palette lives in one place, not in hex strings");

    [Fact]
    public void ProgressRingsOnlyRemainWhereTheyAreListed() =>
        Ratchet(Xaml(), source => source.Contains("<ProgressRing", StringComparison.Ordinal), ProgressRings,
            "nothing on the console spins: work in progress is a busy panel or a travelling segment");

    [Fact]
    public void ChromeBezelsOnlyRemainWhereTheyAreListed() =>
        Ratchet(Xaml(), source => source.Contains("GradientStop Color=\"#707070\"", StringComparison.Ordinal), ChromeBezels,
            "a button is a cap styled once, not a gradient border copied round it");

    [Fact]
    public void FontNamesOnlyRemainWhereTheyAreListed() =>
        Ratchet(Xaml(), source => FontFamilyLiteral.IsMatch(source), FontFamilyLiterals,
            "a face is a named font resource, so a missing file is one fix and not forty");

    [Fact]
    public void DialogsBuiltByHandOnlyRemainWhereTheyAreListed() =>
        Ratchet(Code(AppFolder).Where(file => !file.EndsWith("ConsoleDialog.cs", StringComparison.Ordinal)),
            source => source.Contains("new ContentDialog", StringComparison.Ordinal), HandBuiltDialogs,
            "a dialog built by hand misses the dialog style and the theme; ConsoleDialog gives it both");

    private static void Ratchet(IEnumerable<string> files, Func<string, bool> breaksRule, string[] listed, string because)
    {
        var breaking = files.Where(file => breaksRule(File.ReadAllText(file))).Select(RepoSource.Relative)
            .ToHashSet(StringComparer.Ordinal);

        breaking.Except(listed).Should().BeEmpty($"these break the rule and are not listed: {because}");
        listed.Except(breaking).Should().BeEmpty("these no longer break the rule; take them off the list, which only shrinks");
    }

    private static IEnumerable<string> Xaml() => RepoSource.FilesUnder(AppFolder, "*.xaml");

    private static IEnumerable<string> Code(string folder) => RepoSource.FilesUnder(folder);

    // ---------------------------------------------------------------- what is left

    private static readonly string[] XamlColourLiterals =
    [
        "src/SysMonitor.App/MainWindow.xaml",
        "src/SysMonitor.App/Styles/Colors.xaml",
        "src/SysMonitor.App/Styles/Styles.xaml",
        "src/SysMonitor.App/Views/BackupPage.xaml",
        "src/SysMonitor.App/Views/BatteryPage.xaml",
        "src/SysMonitor.App/Views/BluetoothPage.xaml",
        "src/SysMonitor.App/Views/BrowserPrivacyPage.xaml",
        "src/SysMonitor.App/Views/CleanerPage.xaml",
        "src/SysMonitor.App/Views/CpuPage.xaml",
        "src/SysMonitor.App/Views/DashboardPage.xaml",
        "src/SysMonitor.App/Views/DiskPage.xaml",
        "src/SysMonitor.App/Views/DonationPage.xaml",
        "src/SysMonitor.App/Views/DriverUpdaterPage.xaml",
        "src/SysMonitor.App/Views/DriveWiperPage.xaml",
        "src/SysMonitor.App/Views/DuplicateFinderPage.xaml",
        "src/SysMonitor.App/Views/FileToolsPage.xaml",
        "src/SysMonitor.App/Views/FpsOverlayWindow.xaml",
        "src/SysMonitor.App/Views/GameModePage.xaml",
        "src/SysMonitor.App/Views/GpuPage.xaml",
        "src/SysMonitor.App/Views/HealthCheckPage.xaml",
        "src/SysMonitor.App/Views/HistoryPage.xaml",
        "src/SysMonitor.App/Views/ImageToolsPage.xaml",
        "src/SysMonitor.App/Views/InstalledProgramsPage.xaml",
        "src/SysMonitor.App/Views/LargeFilesPage.xaml",
        "src/SysMonitor.App/Views/MemoryPage.xaml",
        "src/SysMonitor.App/Views/NetworkMapperPage.xaml",
        "src/SysMonitor.App/Views/NetworkPage.xaml",
        "src/SysMonitor.App/Views/PdfEditorPage.xaml",
        "src/SysMonitor.App/Views/PdfToolsPage.xaml",
        "src/SysMonitor.App/Views/PerformancePage.xaml",
        "src/SysMonitor.App/Views/ProcessesPage.xaml",
        "src/SysMonitor.App/Views/RegistryCleanerPage.xaml",
        "src/SysMonitor.App/Views/ScheduledCleaningPage.xaml",
        "src/SysMonitor.App/Views/SettingsPage.xaml",
        "src/SysMonitor.App/Views/StartupPage.xaml",
        "src/SysMonitor.App/Views/SystemInfoPage.xaml",
        "src/SysMonitor.App/Views/TemperaturePage.xaml",
        "src/SysMonitor.App/Views/UserGuidePage.xaml",
        "src/SysMonitor.App/Views/WiFiPage.xaml",
    ];

    private static readonly string[] CodeColourLiterals =
    [
        "src/SysMonitor.App/Converters/Converters.cs",
        "src/SysMonitor.App/ViewModels/BackupViewModel.cs",
        "src/SysMonitor.App/ViewModels/BatteryViewModel.cs",
        "src/SysMonitor.App/ViewModels/BluetoothViewModel.cs",
        "src/SysMonitor.App/ViewModels/CpuViewModel.cs",
        "src/SysMonitor.App/ViewModels/DashboardViewModel.cs",
        "src/SysMonitor.App/ViewModels/DiskViewModel.cs",
        "src/SysMonitor.App/ViewModels/DriverUpdaterViewModel.cs",
        "src/SysMonitor.App/ViewModels/DuplicateFinderViewModel.cs",
        "src/SysMonitor.App/ViewModels/FileToolsViewModel.cs",
        "src/SysMonitor.App/ViewModels/GameModeViewModel.cs",
        "src/SysMonitor.App/ViewModels/GpuViewModel.cs",
        "src/SysMonitor.App/ViewModels/ImageToolsViewModel.cs",
        "src/SysMonitor.App/ViewModels/LargeFilesViewModel.cs",
        "src/SysMonitor.App/ViewModels/MemoryViewModel.cs",
        "src/SysMonitor.App/ViewModels/NetworkMapperViewModel.cs",
        "src/SysMonitor.App/ViewModels/NetworkViewModel.cs",
        "src/SysMonitor.App/ViewModels/PdfEditorViewModel.cs",
        "src/SysMonitor.App/ViewModels/PdfToolsViewModel.cs",
        "src/SysMonitor.App/ViewModels/RegistryCleanerViewModel.cs",
        "src/SysMonitor.App/ViewModels/SystemInfoViewModel.cs",
        "src/SysMonitor.App/ViewModels/TemperatureViewModel.cs",
        "src/SysMonitor.App/ViewModels/WiFiViewModel.cs",
        "src/SysMonitor.Core/Services/Utilities/BluetoothAnalyzer.cs",
        "src/SysMonitor.Core/Services/Utilities/DriverUpdater.cs",
        "src/SysMonitor.Core/Services/Utilities/HealthCheckService.cs",
        "src/SysMonitor.Core/Services/Utilities/IDriverUpdater.cs",
        "src/SysMonitor.Core/Services/Utilities/IInstalledProgramsService.cs",
        "src/SysMonitor.Core/Services/Utilities/IPdfTools.cs",
        "src/SysMonitor.Core/Services/Utilities/IWirelessAnalyzer.cs",
        "src/SysMonitor.Core/Services/Utilities/NetworkMapper.cs",
        "src/SysMonitor.Core/Services/Utilities/WiFiAnalyzer.cs",
    ];

    private static readonly string[] ProgressRings =
    [
        "src/SysMonitor.App/Views/BackupPage.xaml",
        "src/SysMonitor.App/Views/BluetoothPage.xaml",
        "src/SysMonitor.App/Views/DashboardPage.xaml",
        "src/SysMonitor.App/Views/DiskPage.xaml",
        "src/SysMonitor.App/Views/DriverUpdaterPage.xaml",
        "src/SysMonitor.App/Views/FileToolsPage.xaml",
        "src/SysMonitor.App/Views/GameModePage.xaml",
        "src/SysMonitor.App/Views/GpuPage.xaml",
        "src/SysMonitor.App/Views/HistoryPage.xaml",
        "src/SysMonitor.App/Views/ImageToolsPage.xaml",
        "src/SysMonitor.App/Views/MemoryPage.xaml",
        "src/SysMonitor.App/Views/NetworkMapperPage.xaml",
        "src/SysMonitor.App/Views/PdfEditorPage.xaml",
        "src/SysMonitor.App/Views/PdfToolsPage.xaml",
        "src/SysMonitor.App/Views/ScheduledCleaningPage.xaml",
        "src/SysMonitor.App/Views/SystemInfoPage.xaml",
        "src/SysMonitor.App/Views/TemperaturePage.xaml",
        "src/SysMonitor.App/Views/WiFiPage.xaml",
    ];

    private static readonly string[] ChromeBezels =
    [
        "src/SysMonitor.App/Views/BluetoothPage.xaml",
        "src/SysMonitor.App/Views/BrowserPrivacyPage.xaml",
        "src/SysMonitor.App/Views/CleanerPage.xaml",
        "src/SysMonitor.App/Views/DashboardPage.xaml",
        "src/SysMonitor.App/Views/DonationPage.xaml",
        "src/SysMonitor.App/Views/DriverUpdaterPage.xaml",
        "src/SysMonitor.App/Views/DriveWiperPage.xaml",
        "src/SysMonitor.App/Views/DuplicateFinderPage.xaml",
        "src/SysMonitor.App/Views/FileToolsPage.xaml",
        "src/SysMonitor.App/Views/GameModePage.xaml",
        "src/SysMonitor.App/Views/HealthCheckPage.xaml",
        "src/SysMonitor.App/Views/HistoryPage.xaml",
        "src/SysMonitor.App/Views/ImageToolsPage.xaml",
        "src/SysMonitor.App/Views/InstalledProgramsPage.xaml",
        "src/SysMonitor.App/Views/LargeFilesPage.xaml",
        "src/SysMonitor.App/Views/MemoryPage.xaml",
        "src/SysMonitor.App/Views/NetworkMapperPage.xaml",
        "src/SysMonitor.App/Views/PerformancePage.xaml",
        "src/SysMonitor.App/Views/ProcessesPage.xaml",
        "src/SysMonitor.App/Views/RegistryCleanerPage.xaml",
        "src/SysMonitor.App/Views/ScheduledCleaningPage.xaml",
        "src/SysMonitor.App/Views/SettingsPage.xaml",
        "src/SysMonitor.App/Views/StartupPage.xaml",
        "src/SysMonitor.App/Views/WiFiPage.xaml",
    ];

    private static readonly string[] FontFamilyLiterals =
    [
        "src/SysMonitor.App/Views/FpsOverlayWindow.xaml",
        "src/SysMonitor.App/Views/GameModePage.xaml",
        "src/SysMonitor.App/Views/NetworkPage.xaml",
        "src/SysMonitor.App/Views/PdfToolsPage.xaml",
        "src/SysMonitor.App/Views/SettingsPage.xaml",
        "src/SysMonitor.App/Views/SystemInfoPage.xaml",
        "src/SysMonitor.App/Views/UserGuidePage.xaml",
    ];

    private static readonly string[] HandBuiltDialogs =
    [
        "src/SysMonitor.App/Views/BackupPage.xaml.cs",
        "src/SysMonitor.App/Views/GameModePage.xaml.cs",
        "src/SysMonitor.App/Views/PdfEditorPage.xaml.cs",
        "src/SysMonitor.App/Views/RegistryCleanerPage.xaml.cs",
    ];
}
