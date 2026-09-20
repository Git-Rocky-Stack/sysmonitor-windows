using FluentAssertions;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// What the wiper writes has to be what it says it writes, and it has to check the writing landed. The
/// "Gutmann" option used to fill each pass with (pass * 17) % 256, which is not that method at all.
/// </summary>
public class DriveWiperPatternTests : IDisposable
{
    private readonly TempDirectory _temp = new("wiper-patterns");

    public void Dispose() => _temp.Dispose();

    [Theory]
    // The table from Gutmann's paper: passes 5-31, in order.
    [InlineData(5, new byte[] { 0x55, 0x55, 0x55 })]
    [InlineData(6, new byte[] { 0xAA, 0xAA, 0xAA })]
    [InlineData(7, new byte[] { 0x92, 0x49, 0x24 })]
    [InlineData(8, new byte[] { 0x49, 0x24, 0x92 })]
    [InlineData(9, new byte[] { 0x24, 0x92, 0x49 })]
    [InlineData(10, new byte[] { 0x00, 0x00, 0x00 })]
    [InlineData(17, new byte[] { 0x77, 0x77, 0x77 })]
    [InlineData(25, new byte[] { 0xFF, 0xFF, 0xFF })]
    [InlineData(29, new byte[] { 0x6D, 0xB6, 0xDB })]
    [InlineData(30, new byte[] { 0xB6, 0xDB, 0x6D })]
    [InlineData(31, new byte[] { 0xDB, 0x6D, 0xB6 })]
    public void TheGutmannPassesWriteTheGutmannPatterns(int pass, byte[] expected)
    {
        var buffer = DriveWiper.GetWipePattern(WipeMethod.Gutmann, pass);

        buffer.Take(9).Should().Equal(expected.Concat(expected).Concat(expected), "the pattern repeats across the buffer");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(32)]
    [InlineData(35)]
    public void TheGutmannPassesAroundThePatternsAreRandom(int pass)
    {
        var first = DriveWiper.GetWipePattern(WipeMethod.Gutmann, pass);
        var second = DriveWiper.GetWipePattern(WipeMethod.Gutmann, pass);

        first.Should().NotEqual(second, "these passes are random data, not a pattern");
    }

    [Fact]
    public void TheGutmannTableIsTheTwentySevenPatternsBetweenTheRandomPasses()
    {
        DriveWiper.GutmannPatterns.Should().HaveCount(27);
        DriveWiper.GetPassCount(WipeMethod.Gutmann).Should().Be(35);
    }

    [Theory]
    [InlineData(WipeMethod.SinglePass, 1, 0x00)]
    [InlineData(WipeMethod.DoD3Pass, 1, 0x00)]
    [InlineData(WipeMethod.DoD3Pass, 2, 0xFF)]
    [InlineData(WipeMethod.DoD7Pass, 4, 0x96)]
    public void TheOtherMethodsWriteWhatTheyDescribe(WipeMethod method, int pass, byte expected) =>
        DriveWiper.GetWipePattern(method, pass).Take(64).Should().AllBeEquivalentTo(expected);

    [Fact]
    public async Task ReadingBackConfirmsAPassThatLanded()
    {
        var path = _temp.File("landed.bin", new string('x', 5000));
        var pattern = DriveWiper.GetWipePattern(WipeMethod.Gutmann, 7);
        await File.WriteAllBytesAsync(path, Enumerable.Range(0, 5000).Select(i => pattern[i % pattern.Length]).ToArray());

        (await DriveWiper.VerifyOverwriteAsync(path, pattern, CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task ReadingBackCatchesAPassThatDidNot()
    {
        var path = _temp.File("not-landed.bin", new string('x', 5000));
        var pattern = DriveWiper.GetWipePattern(WipeMethod.DoD3Pass, 2);   // 0xFF

        (await DriveWiper.VerifyOverwriteAsync(path, pattern, CancellationToken.None))
            .Should().BeFalse("the file still holds what was there before");
    }

    [Fact]
    public async Task AWipedFileIsReportedAsVerified()
    {
        var path = _temp.File("secret.txt", "something private");
        var wiper = new DriveWiper(Microsoft.Extensions.Logging.Abstractions.NullLogger<DriveWiper>.Instance);

        var result = await wiper.SecureDeleteFileAsync(path, WipeMethod.SinglePass);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.FilesWiped.Should().Be(1);
        result.FilesVerified.Should().Be(1, "the last pass was read back before the file was removed");
        result.FailedPaths.Should().BeEmpty();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task AWipeWhoseLastPassCannotBeReadBackIsNotCalledVerified()
    {
        // A file this account may write but not read: the overwrite still happens, the check cannot.
        var path = _temp.File("unreadable.bin", new string('x', 4096));
        var info = new FileInfo(path);
        var security = info.GetAccessControl();
        var account = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            account, System.Security.AccessControl.FileSystemRights.ReadData,
            System.Security.AccessControl.AccessControlType.Deny));
        info.SetAccessControl(security);

        var wiper = new DriveWiper(Microsoft.Extensions.Logging.Abstractions.NullLogger<DriveWiper>.Instance);
        var result = await wiper.SecureDeleteFileAsync(path, WipeMethod.SinglePass);

        result.FilesVerified.Should().Be(0, "nothing confirmed the overwrite landed");
        result.FailedPaths.Should().ContainSingle().Which.Should().Contain("unconfirmed");
    }

    [Fact]
    public void ThePatternsAreLaidDownRepeatedlyAcrossABuffer()
    {
        var buffer = new byte[10];

        DriveWiper.FillRepeating(buffer, [1, 2, 3]);

        buffer.Should().Equal([1, 2, 3, 1, 2, 3, 1, 2, 3, 1]);
    }
}
