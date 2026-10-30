using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// The window is called what the product is called.
/// <para>
/// <c>MainWindow.xaml</c> was renamed to STX-1 ULTIMATE System Monitor, and its constructor went on setting
/// <c>Title = "SysMonitor - Windows System Monitor &amp; Optimizer"</c> straight after
/// <c>InitializeComponent</c> - so the taskbar, Alt+Tab and the title bar in every screenshot carried a
/// name the app, installer and Store listing no longer use. The title lives in one place, the XAML.
/// </para>
/// </summary>
public class WindowTitleTests
{
    private const string ProductName = "STX-1 ULTIMATE System Monitor";

    [Fact]
    public void TheMainWindowIsTitledWithTheProductsName()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoSource.Root, "src", "SysMonitor.App", "MainWindow.xaml"));

        var title = Regex.Match(xaml, @"<Window\b[^>]*?\bTitle=""(?<t>[^""]*)""", RegexOptions.Singleline);
        title.Success.Should().BeTrue("MainWindow.xaml declares the window's title");
        title.Groups["t"].Value.Should().Be(ProductName);
    }

    [Fact]
    public void NoCodeOverwritesTheWindowsTitle()
    {
        var code = File.ReadAllText(Path.Combine(RepoSource.Root, "src", "SysMonitor.App", "MainWindow.xaml.cs"));

        Regex.IsMatch(code, @"^\s*(this\.)?Title\s*=", RegexOptions.Multiline).Should().BeFalse(
            "the title is set once, in MainWindow.xaml; a second assignment in code wins and goes stale unseen");
    }
}
