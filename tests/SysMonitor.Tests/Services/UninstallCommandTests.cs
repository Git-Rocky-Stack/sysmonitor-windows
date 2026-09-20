using System.Diagnostics;
using FluentAssertions;
using SysMonitor.Core.Services.Utilities;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// An UninstallString from the registry has to be split into a program and its arguments exactly as Windows
/// would, and what the uninstaller reports afterwards has to be read for what it means.
/// </summary>
public class UninstallCommandTests
{
    [Theory]
    // The shape most entries on a machine have: cutting seven characters in used to leave ".exe /X{...}".
    [InlineData(@"MsiExec.exe /X{0EFDDD81-1F1E-4A29-B9C6-B5D1A7F1DD8F}", "MsiExec.exe", "/X{0EFDDD81-1F1E-4A29-B9C6-B5D1A7F1DD8F} /quiet /norestart")]
    [InlineData(@"C:\WINDOWS\system32\MsiExec.exe /I{5D2F-1}", @"C:\WINDOWS\system32\MsiExec.exe", "/I{5D2F-1} /quiet /norestart")]
    [InlineData(@"msiexec /x {5D2F-1}", "msiexec", "/x {5D2F-1} /quiet /norestart")]
    // A quoted program, with and without arguments.
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\" /SILENT", @"C:\Program Files\App\unins000.exe", "/SILENT")]
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\"", @"C:\Program Files\App\unins000.exe", "")]
    // An unquoted path with spaces: the program ends at the extension, not at the first space.
    [InlineData(@"C:\Program Files\App\unins000.exe /SILENT /NORESTART", @"C:\Program Files\App\unins000.exe", "/SILENT /NORESTART")]
    [InlineData(@"C:\App\setup.exe", @"C:\App\setup.exe", "")]
    // A folder whose name merely starts with an extension is not the program.
    [InlineData(@"C:\Tools\weird.executables\unins.exe /S", @"C:\Tools\weird.executables\unins.exe", "/S")]
    // Nothing that looks like a program: fall back to the first word, as Windows does.
    [InlineData(@"RunDll32 C:\W\setup.dll,Uninstall", "RunDll32", @"C:\W\setup.dll,Uninstall")]
    public void AnUninstallStringIsSplitIntoAProgramAndItsArguments(string uninstallString, string expectedProgram, string expectedArguments)
    {
        var command = InstalledProgramsService.ParseUninstallCommand(uninstallString);

        command.FileName.Should().Be(expectedProgram);
        command.Arguments.Should().Be(expectedArguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\"C:\\unterminated\\quote.exe /S")]
    public void AnUnreadableUninstallStringYieldsNoProgram(string uninstallString) =>
        InstalledProgramsService.ParseUninstallCommand(uninstallString).FileName.Should().BeEmpty();

    [Theory]
    [InlineData("/X{5D2F-1}", "/X{5D2F-1} /quiet /norestart")]
    [InlineData("", "/quiet /norestart")]
    // A command that already says how to behave is left as it is.
    [InlineData("/X{5D2F-1} /qn", "/X{5D2F-1} /qn")]
    [InlineData("/X{5D2F-1} /passive", "/X{5D2F-1} /passive")]
    [InlineData("/X{5D2F-1} /QB", "/X{5D2F-1} /QB")]
    public void WindowsInstallerIsToldNotToAskUnlessTheCommandAlreadyDoes(string arguments, string expected) =>
        InstalledProgramsService.EnsureWindowsInstallerIsQuiet(arguments).Should().Be(expected);

    [Fact]
    public void AProgramThatIsNotWindowsInstallerKeepsItsArguments() =>
        InstalledProgramsService.ParseUninstallCommand(@"C:\App\unins000.exe /S").Arguments.Should().Be("/S");

    [Theory]
    [InlineData(0, true)]
    [InlineData(3010, true)]   // uninstalled, restart required
    [InlineData(1641, true)]   // uninstalled, restart started
    [InlineData(1605, true)]   // it was not installed any more
    [InlineData(1602, false)]  // cancelled
    [InlineData(1603, false)]  // fatal error
    [InlineData(1618, false)]  // another installation is running
    [InlineData(7, false)]
    public void AnUninstallerSExitCodeIsReadForWhatItMeans(int exitCode, bool expectedSuccess)
    {
        var result = InstalledProgramsService.DescribeExitCode(exitCode, "Test App");

        result.Success.Should().Be(expectedSuccess);
        result.ExitCode.Should().Be(exitCode);
        result.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ARestartCodeSaysARestartIsNeeded() =>
        InstalledProgramsService.DescribeExitCode(3010, "Test App").Message.Should().Contain("Restart");

    [Fact]
    public void AnAlreadyGoneProgramIsNotReportedAsAFailure() =>
        InstalledProgramsService.DescribeExitCode(1605, "Test App").Message.Should().Contain("not installed any more");

    [Theory]
    [InlineData(0, true)]
    [InlineData(3010, true)]
    [InlineData(1603, false)]
    public async Task AnUninstallerThatFinishesIsReportedByWhatItReturned(int exitCode, bool expectedSuccess)
    {
        using var process = Start($"/c exit {exitCode}");

        var result = await InstalledProgramsService.WaitForUninstallerAsync(process, "Test App", TimeSpan.FromSeconds(30));

        result.Success.Should().Be(expectedSuccess);
        result.ExitCode.Should().Be(exitCode);
    }

    [Fact]
    public async Task AnUninstallerStillRunningIsSaidToBeRunningAndLeftAlone()
    {
        using var process = Start("/c ping -n 30 127.0.0.1");
        try
        {
            var result = await InstalledProgramsService.WaitForUninstallerAsync(process, "Test App", TimeSpan.FromSeconds(1));

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("still running");
            process.HasExited.Should().BeFalse("the uninstaller is left to finish on its own");
        }
        finally
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
        }
    }

    private static Process Start(string arguments) => Process.Start(new ProcessStartInfo
    {
        FileName = "cmd.exe",
        Arguments = arguments,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardOutput = true,
    })!;
}
