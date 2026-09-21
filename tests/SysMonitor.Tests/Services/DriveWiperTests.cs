using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

public class DriveWiperTests
{
    private readonly DriveWiper _wiper = new(NullLogger<DriveWiper>.Instance);

    [Fact]
    public async Task SecureDeleteDirectory_DoesNotFollowJunction_TargetKeepsItsContent()
    {
        using var selected = new TempDirectory("wipe-selected");
        using var outside = new TempDirectory("wipe-outside");
        var victim = outside.File("victim.txt", "must survive");
        selected.File("inside.txt", "wipe me");
        FileSystemLinks.CreateJunction(Path.Combine(selected.Path, "link-out"), outside.Path);

        var result = await _wiper.SecureDeleteDirectoryAsync(selected.Path, WipeMethod.SinglePass);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.FilesWiped.Should().Be(1);
        result.LinksRemoved.Should().Be(1);
        File.ReadAllText(victim).Should().Be("must survive");
        Directory.Exists(selected.Path).Should().BeFalse();
    }

    [Fact]
    public async Task SecureDeleteDirectory_DoesNotFollowFileSymlink_TargetKeepsItsContent()
    {
        using var selected = new TempDirectory("wipe-selected");
        using var outside = new TempDirectory("wipe-outside");
        var victim = outside.File("victim.txt", "must survive");
        FileSystemLinks.CreateFileSymlink(Path.Combine(selected.Path, "link.txt"), victim);

        var result = await _wiper.SecureDeleteDirectoryAsync(selected.Path, WipeMethod.SinglePass);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.FilesWiped.Should().Be(0);
        result.LinksRemoved.Should().Be(1);
        File.ReadAllText(victim).Should().Be("must survive");
    }

    [Fact]
    public async Task SecureDeleteFile_OnSymlink_RefusesAndLeavesTargetIntact()
    {
        using var selected = new TempDirectory("wipe-selected");
        using var outside = new TempDirectory("wipe-outside");
        var victim = outside.File("victim.txt", "must survive");
        var link = Path.Combine(selected.Path, "link.txt");
        FileSystemLinks.CreateFileSymlink(link, victim);

        var result = await _wiper.SecureDeleteFileAsync(link, WipeMethod.SinglePass);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("symbolic link");
        File.ReadAllText(victim).Should().Be("must survive");
    }

    [Fact]
    public async Task SecureDeleteDirectory_RootIsJunction_RefusesWithoutTouchingTarget()
    {
        using var parent = new TempDirectory("wipe-parent");
        using var outside = new TempDirectory("wipe-outside");
        var victim = outside.File("victim.txt", "must survive");
        var link = Path.Combine(parent.Path, "link");
        FileSystemLinks.CreateJunction(link, outside.Path);

        var result = await _wiper.SecureDeleteDirectoryAsync(link, WipeMethod.SinglePass);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("link to another location");
        File.ReadAllText(victim).Should().Be("must survive");
    }

    [Fact]
    public async Task SecureDeleteDirectory_WipesNestedHiddenAndRegularFiles_AndRemovesTree()
    {
        using var selected = new TempDirectory("wipe-selected");
        selected.File("a.txt", "1");
        selected.File(Path.Combine("sub", "b.txt"), "2");
        selected.File(Path.Combine("sub", "deeper", "c.bin"), new string('x', 5000));
        var hidden = selected.File("hidden.txt", "3");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.ReadOnly);

        var result = await _wiper.SecureDeleteDirectoryAsync(selected.Path, WipeMethod.SinglePass);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.FilesWiped.Should().Be(4);
        result.BytesWiped.Should().Be(1 + 1 + 5000 + 1);
        Directory.Exists(selected.Path).Should().BeFalse();
    }

    [Fact]
    public async Task SecureDeleteDirectory_ReportsFailure_WhenAFileCannotBeWiped()
    {
        using var selected = new TempDirectory("wipe-selected");
        var locked = selected.File("locked.txt", "keep");
        selected.File("other.txt", "wipe");

        WipeResult result;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await _wiper.SecureDeleteDirectoryAsync(selected.Path, WipeMethod.SinglePass);
        }

        result.Success.Should().BeFalse();
        result.FilesWiped.Should().Be(1);
        result.FailedPaths.Should().Contain(locked);
        result.ErrorMessage.Should().Contain("could not be wiped");
        File.ReadAllText(locked).Should().Be("keep");
    }

    [Theory]
    [InlineData(Environment.SpecialFolder.Windows)]
    [InlineData(Environment.SpecialFolder.ProgramFiles)]
    [InlineData(Environment.SpecialFolder.ProgramFilesX86)]
    [InlineData(Environment.SpecialFolder.CommonApplicationData)]
    public void IsProtectedLocation_SystemFoldersAndTheirChildren_AreProtected(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);

        DriveWiper.IsProtectedLocation(path).Should().BeTrue();
        DriveWiper.IsProtectedLocation(Path.Combine(path, "SomeChild")).Should().BeTrue();
    }

    [Fact]
    public void IsProtectedLocation_SystemDriveRootProtected_UserFoldersNot()
    {
        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows))!;

        DriveWiper.IsProtectedLocation(systemRoot).Should().BeTrue();
        DriveWiper.IsProtectedLocation(Path.GetTempPath()).Should().BeFalse();
        DriveWiper.IsProtectedLocation(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)).Should().BeFalse();
    }
}
