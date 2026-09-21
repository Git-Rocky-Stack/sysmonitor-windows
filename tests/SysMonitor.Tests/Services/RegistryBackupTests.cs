using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using Microsoft.Win32;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

public class RegistryBackupTests : IDisposable
{
    private readonly TempDirectory _backupFolder = new("regbackups");
    private readonly TestRegistryKey _root = new();
    private readonly RegistryCleaner _cleaner;

    public RegistryBackupTests()
    {
        _cleaner = new RegistryCleaner(_backupFolder.Path);
    }

    public void Dispose()
    {
        _root.Dispose();
        _backupFolder.Dispose();
    }

    private RegistryIssue ValueIssue(string valueName) => new()
    {
        Key = _root.FullName,
        ValueName = valueName,
        Category = RegistryIssueCategory.InvalidStartupEntry,
        IsSelected = true
    };

    private RegistryIssue SubkeyIssue(string subKeyName) => new()
    {
        Key = $@"{_root.FullName}\{subKeyName}",
        ValueName = "InstallLocation",
        Category = RegistryIssueCategory.OrphanedSoftware,
        IsSelected = true
    };

    private void Populate()
    {
        _root.Key.SetValue("Str", "hello");
        _root.Key.SetValue("Dw", 42, RegistryValueKind.DWord);
        _root.Key.SetValue("Qw", 1L << 40, RegistryValueKind.QWord);
        _root.Key.SetValue("Multi", new[] { "a", "b" }, RegistryValueKind.MultiString);
        _root.Key.SetValue("Exp", @"%TEMP%\x", RegistryValueKind.ExpandString);
        using var orphan = _root.Key.CreateSubKey("Orphan");
        orphan.SetValue("DisplayName", "Removed App");
        orphan.SetValue("Bin", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);
        using var deeper = orphan.CreateSubKey("Deeper");
        deeper.SetValue("X", "y");
    }

    [Fact]
    public async Task Backup_Clean_Restore_RoundTripsKeysAndEveryValueType()
    {
        Populate();
        var issues = new[] { ValueIssue("Str"), SubkeyIssue("Orphan") };

        var backup = await _cleaner.BackupRegistryAsync(issues);
        backup.Success.Should().BeTrue(backup.Message);
        backup.KeysExported.Should().Be(2, "each issue's key is exported on its own");

        var clean = await _cleaner.CleanAsync(issues);
        clean.FilesDeleted.Should().Be(2);
        using (var afterClean = Registry.CurrentUser.OpenSubKey(_root.SubPath)!)
        {
            afterClean.GetValue("Str").Should().BeNull();
            afterClean.OpenSubKey("Orphan").Should().BeNull();
        }

        var restore = await _cleaner.RestoreRegistryBackupAsync(backup.BackupPath!);
        restore.Success.Should().BeTrue(restore.Message);

        using var key = Registry.CurrentUser.OpenSubKey(_root.SubPath)!;
        key.GetValue("Str").Should().Be("hello");
        key.GetValue("Dw").Should().Be(42);
        key.GetValue("Qw").Should().Be(1L << 40);
        key.GetValue("Multi").Should().BeEquivalentTo(new[] { "a", "b" });
        key.GetValue("Exp", null, RegistryValueOptions.DoNotExpandEnvironmentNames).Should().Be(@"%TEMP%\x");
        using var orphan = key.OpenSubKey("Orphan")!;
        orphan.GetValue("DisplayName").Should().Be("Removed App");
        orphan.GetValue("Bin").Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        using var deeper = orphan.OpenSubKey("Deeper")!;
        deeper.GetValue("X").Should().Be("y");
    }

    [Fact]
    public async Task Backup_WithOnlyMissingOrUnselectedKeys_WritesNothing()
    {
        Populate();
        var unselected = SubkeyIssue("Orphan"); // exists, but the user did not select it
        unselected.IsSelected = false;

        var backup = await _cleaner.BackupRegistryAsync([SubkeyIssue("DoesNotExist"), unselected]);

        backup.Success.Should().BeTrue(backup.Message);
        backup.KeysExported.Should().Be(0);
        backup.BackupPath.Should().BeNull();
        Directory.EnumerateFiles(_backupFolder.Path).Should().BeEmpty();
    }

    [Fact]
    public async Task Backup_WhenAKeyToDeleteCannotBeRead_FailsAndWritesNothing()
    {
        Populate();
        using (_root.Key.CreateSubKey("Locked")) { }
        var denyRead = DenyRead();
        SetRule("Locked", denyRead, add: true);

        try
        {
            var backup = await _cleaner.BackupRegistryAsync([ValueIssue("Str"), SubkeyIssue("Locked")]);

            backup.Success.Should().BeFalse();
            backup.FailedKeys.Should().ContainSingle(k => k.Contains("Locked"));
            backup.Message.Should().Contain("nothing was changed");
            Directory.EnumerateFiles(_backupFolder.Path).Should().BeEmpty();
        }
        finally
        {
            SetRule("Locked", denyRead, add: false);
        }
    }

    [Fact]
    public async Task Backup_WhenADescendantOfAKeyToDeleteCannotBeRead_Fails()
    {
        // reg.exe exports a parent with exit code 0 while silently omitting children it cannot read;
        // deleting the parent would then lose data the backup never contained.
        Populate();
        var denyRead = DenyRead();
        SetRule(@"Orphan\Deeper", denyRead, add: true);

        try
        {
            var backup = await _cleaner.BackupRegistryAsync([SubkeyIssue("Orphan")]);

            backup.Success.Should().BeFalse();
            backup.FailedKeys.Should().ContainSingle(k => k.Contains(@"Orphan\Deeper"));
        }
        finally
        {
            SetRule(@"Orphan\Deeper", denyRead, add: false);
        }
    }

    [Fact]
    public async Task Backup_ForValueFixes_DoesNotRequireUnrelatedSubkeysToBeReadable()
    {
        Populate();
        using (_root.Key.CreateSubKey("Locked")) { }
        var denyRead = DenyRead();
        SetRule("Locked", denyRead, add: true);

        try
        {
            var backup = await _cleaner.BackupRegistryAsync([ValueIssue("Str")]);

            backup.Success.Should().BeTrue(backup.Message);
            File.ReadAllText(backup.BackupPath!).Should().Contain("\"Str\"=\"hello\"");
        }
        finally
        {
            SetRule("Locked", denyRead, add: false);
        }
    }

    [Fact]
    public async Task GetBackups_ListsNewestFirst()
    {
        Populate();
        var first = await _cleaner.BackupRegistryAsync([ValueIssue("Str")]);
        await Task.Delay(20);
        var second = await _cleaner.BackupRegistryAsync([ValueIssue("Dw")]);

        _cleaner.GetBackups().Should().Equal(second.BackupPath, first.BackupPath);
    }

    [Fact]
    public async Task Restore_RejectsFilesThatAreNotRegistryBackups()
    {
        var notABackup = _backupFolder.File("notes.reg", "just some text");

        var restore = await _cleaner.RestoreRegistryBackupAsync(notABackup);

        restore.Success.Should().BeFalse();
        restore.Message.Should().Contain("not a registry backup");
    }

    /// <summary>
    /// The backup folder is under %LocalAppData%, which any process running as this user can write to. A
    /// machine-wide restore hands the file to reg.exe behind a UAC prompt, so a file dropped there that the
    /// app never wrote is a way into HKLM with administrator rights - a prompt the user attributes to this
    /// app, spending it on someone else's payload.
    /// <para>
    /// A .reg file is refused unless its contents match what this app recorded when it wrote that backup.
    /// The refusal happens before anything is launched, which is also what keeps this test from raising a
    /// UAC prompt of its own.
    /// </para>
    /// </summary>
    /// <summary>
    /// A harmless key stands in for the payload on purpose. The real attack writes an Image File Execution
    /// Options debugger and gets SYSTEM; a test that did that would arm the backdoor on this machine every
    /// time the guard regressed. This proves the same thing - a machine-wide file the app did not write is
    /// turned away - and the absence check below is what catches a regression, safely.
    /// </summary>
    private const string CanaryKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\SysMonitor.Tests\MustNeverBeImported";

    [Fact]
    public async Task Restore_RefusesAMachineWideFileTheAppNeverWrote()
    {
        var planted = _backupFolder.File("registry_backup_2099-01-01_00-00-00-000.reg",
            "Windows Registry Editor Version 5.00\r\n\r\n" +
            $"[{CanaryKey}]\r\n\"Imported\"=\"yes\"\r\n");

        var restore = await _cleaner.RestoreRegistryBackupAsync(planted);

        restore.Success.Should().BeFalse("nothing this app did not write may be imported into HKLM under its UAC prompt");
        restore.Message.Should().Contain("not one this app wrote");
        CanaryWasImported().Should().BeFalse("the refusal must happen before anything is handed to reg.exe");
    }

    /// <summary>Reads the canary without creating it, so the check itself cannot cause what it looks for.</summary>
    private static bool CanaryWasImported()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\SysMonitor.Tests\MustNeverBeImported");
        return key is not null;
    }

    /// <summary>The same guard must not turn away a genuine backup - otherwise recovery is gone too.</summary>
    [Fact]
    public async Task Restore_StillAcceptsABackupTheAppItselfWrote()
    {
        Populate();
        var backup = await _cleaner.BackupRegistryAsync([ValueIssue("Str")]);
        backup.Success.Should().BeTrue(backup.Message);

        var restore = await _cleaner.RestoreRegistryBackupAsync(backup.BackupPath!);

        restore.Success.Should().BeTrue(restore.Message);
    }

    /// <summary>A recorded backup edited afterwards is no longer the backup that was recorded.</summary>
    [Fact]
    public async Task Restore_RefusesARecordedBackupThatWasEditedAfterwards()
    {
        Populate();
        var backup = await _cleaner.BackupRegistryAsync([ValueIssue("Str")]);
        backup.Success.Should().BeTrue(backup.Message);

        await File.AppendAllTextAsync(backup.BackupPath!, $"\r\n[{CanaryKey}]\r\n\"Imported\"=\"yes\"\r\n");

        var restore = await _cleaner.RestoreRegistryBackupAsync(backup.BackupPath!);

        restore.Success.Should().BeFalse("the bytes no longer match what was recorded when the backup was written");
        restore.Message.Should().Contain("not one this app wrote");
        CanaryWasImported().Should().BeFalse("an edited backup must be refused before anything is handed to reg.exe");
    }

    private static RegistryAccessRule DenyRead() => new(
        WindowsIdentity.GetCurrent().User!,
        RegistryRights.QueryValues | RegistryRights.EnumerateSubKeys,
        AccessControlType.Deny);

    /// <summary>Adds or removes an ACL rule; opens the key with permission rights only, which the owner always has.</summary>
    private void SetRule(string relativePath, RegistryAccessRule rule, bool add)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{_root.SubPath}\{relativePath}",
            RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.ChangePermissions | RegistryRights.ReadPermissions)!;
        var security = key.GetAccessControl();
        if (add) security.AddAccessRule(rule);
        else security.RemoveAccessRule(rule);
        key.SetAccessControl(security);
    }
}
