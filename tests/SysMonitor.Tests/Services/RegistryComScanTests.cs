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
}
