using FluentAssertions;
using SysMonitor.Core.Services.Cleaners;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Deciding that a COM server's DLL is missing is a decision to delete its whole CLSID subtree, so the
/// evidence has to actually be evidence.
/// <para>
/// A great many COM servers are registered by bare file name - <c>mapi32.dll</c>, <c>mscoree.dll</c> - and
/// Windows finds them through the DLL search order. <c>File.Exists("mapi32.dll")</c> resolves that against
/// the current directory of whatever process is asking, which is not where the DLL lives, so it answers
/// false for a file that is plainly there. On this machine that mistake reached MAPIPSFactory, the proxy
/// factory every MAPI client including Outlook depends on.
/// </para>
/// <para>
/// Absence has to be provable. A path that is not fully qualified is not proof of anything, so it is left
/// alone - a cleaner that cannot tell must not delete.
/// </para>
/// </summary>
public class RegistryComScanTests
{
    [Theory]
    [InlineData("kernel32.dll")]
    [InlineData("MAPI32.DLL")]
    [InlineData("mscoree.dll")]
    public void ABareNameWindowsCanFindIsNotMissing(string registered) =>
        RegistryCleaner.IsMissingComServer(registered).Should().BeFalse(
            "Windows resolves a bare name through the DLL search order, not the current directory");

    [Theory]
    [InlineData("no-such-library-98d2c1.dll")]
    [InlineData(@"..\relative\thing.dll")]
    [InlineData(@"subdir\thing.dll")]
    public void ANameThatIsNotFullyQualifiedIsNeverProofOfAbsence(string registered) =>
        RegistryCleaner.IsMissingComServer(registered).Should().BeFalse(
            "the search order includes directories this scan cannot know about, so absence is unprovable");

    [Fact]
    public void AFullyQualifiedPathThatIsGoneIsMissing() =>
        RegistryCleaner.IsMissingComServer(@"C:\Program Files\NoSuchVendor\NoSuchApp\ghost.dll")
            .Should().BeTrue("a rooted path either exists or it does not, and this one does not");

    [Fact]
    public void AFullyQualifiedPathThatExistsIsNotMissing() =>
        RegistryCleaner.IsMissingComServer(Path.Combine(Environment.SystemDirectory, "kernel32.dll"))
            .Should().BeFalse();

    /// <summary>System32 and SysWOW64 are left alone regardless - the existing exemption, kept.</summary>
    [Theory]
    [InlineData(@"C:\Windows\System32\nosuchfile-98d2c1.dll")]
    [InlineData(@"C:\Windows\SysWOW64\nosuchfile-98d2c1.dll")]
    public void AWindowsSystemPathIsLeftAlone(string registered) =>
        RegistryCleaner.IsMissingComServer(registered).Should().BeFalse();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void NothingRegisteredIsNotAMissingFile(string registered) =>
        RegistryCleaner.IsMissingComServer(registered).Should().BeFalse();

    /// <summary>Environment variables are expanded before the decision, as they always were.</summary>
    [Fact]
    public void AnExpandedSystemPathIsStillRecognisedAsASystemPath() =>
        RegistryCleaner.IsMissingComServer(@"%SystemRoot%\System32\nosuchfile-98d2c1.dll")
            .Should().BeFalse();

    // ------------------------------------------------ the same rule, for every scan that ends in a delete

    /// <summary>
    /// An MSI product's uninstall string names msiexec by bare name. Believing File.Exists about it flagged
    /// every MSI-installed program as orphaned, and the fix deleted its Uninstall key.
    /// </summary>
    [Theory]
    [InlineData("MsiExec.exe /X{6F320B93-EE3C-4826-85E0-ADF79F8D4C61}")]
    [InlineData("ctfmon.exe")]
    [InlineData("rundll32.exe shell32.dll,Control_RunDLL")]
    public void AnUninstallOrStartupCommandNamedByBareNameIsNotMissing(string command) =>
        RegistryCleaner.IsProvablyMissing(RegistryCleaner.ExtractFilePath(command)).Should().BeFalse();

    [Fact]
    public void APathOnADriveThatIsNotMountedProvesNothing() =>
        RegistryCleaner.IsProvablyMissing(UnmountedDrive() + @"Games\Launcher\launcher.exe")
            .Should().BeFalse("a program on an unplugged disk is installed, only out of reach");

    [Fact]
    public void AnExistingFolderCountsWhereAFolderIsAsked()
    {
        var folder = Path.GetTempPath();
        RegistryCleaner.IsProvablyMissing(folder, allowDirectory: true).Should().BeFalse();
        RegistryCleaner.IsProvablyMissing(Path.Combine(folder, "no-such-folder-98d2c1"), allowDirectory: true)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(@"C:\Windows\System32\ieframe.dll\1", @"C:\Windows\System32\ieframe.dll")]
    [InlineData(@"C:\Program Files\App\app.tlb", @"C:\Program Files\App\app.tlb")]
    [InlineData(@"C:\Program Files\App 2\lib.dll\12", @"C:\Program Files\App 2\lib.dll")]
    public void ATypeLibrarysResourceIndexIsNotPartOfItsFile(string registered, string file) =>
        RegistryCleaner.StripResourceIndex(registered).Should().Be(file);

    private static string UnmountedDrive()
    {
        var mounted = DriveInfo.GetDrives().Select(drive => char.ToUpperInvariant(drive.Name[0])).ToHashSet();
        var free = "ZYXWVUTSRQPONMLKJIHGFED".First(letter => !mounted.Contains(letter));
        return $"{free}:\\";
    }
}
