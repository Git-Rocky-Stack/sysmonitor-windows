using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Moq;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// What gets done as administrator must not be decided by something another process can change. The list of
/// registry fixes and the result travel over a named pipe only this account can open - never a file, which a
/// planted link could turn into an administrator's write anywhere - and the elevated side still checks the list
/// against the fingerprint on its command line and against its own scan.
/// </summary>
public class ElevatedHandoffTests : IDisposable
{
    private readonly TempDirectory _temp = new("elevated");

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void AListStillAsApprovedIsAccepted()
    {
        var payload = Encoding.UTF8.GetBytes("[{\"Key\":\"HKEY_LOCAL_MACHINE\\\\SOFTWARE\\\\Broken\"}]");

        ElevatedRegistryHelper.MatchesFingerprint(payload, Fingerprint(payload)).Should().BeTrue();
    }

    [Fact]
    public void AListSwappedAfterApprovalIsRefused()
    {
        var approved = Encoding.UTF8.GetBytes("[{\"Key\":\"HKEY_LOCAL_MACHINE\\\\SOFTWARE\\\\Broken\"}]");
        var swapped = Encoding.UTF8.GetBytes("[{\"Key\":\"HKEY_LOCAL_MACHINE\\\\SYSTEM\"}]");

        ElevatedRegistryHelper.MatchesFingerprint(swapped, Fingerprint(approved)).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-hex")]
    [InlineData("00")]
    public void AMissingOrMalformedFingerprintIsRefused(string fingerprint) =>
        ElevatedRegistryHelper.MatchesFingerprint([1, 2, 3], fingerprint).Should().BeFalse();

    [Fact]
    public async Task TheElevatedSideCleansNothingWhenTheListNoLongerMatches()
    {
        var approved = Encoding.UTF8.GetBytes("[]");

        // What an attacker would put in place of the approved list.
        var swapped = JsonSerializer.SerializeToUtf8Bytes(new[]
        {
            new RegistryIssue { Key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet", Category = RegistryIssueCategory.OrphanedSoftware },
        });

        var (exitCode, result) = await ElevatedRegistryHelper.CleanApprovedAsync(swapped, Fingerprint(approved));

        exitCode.Should().Be(1);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("changed after it was approved");
        result.FixedCount.Should().Be(0);
    }

    [Fact]
    public async Task TheListAndTheResultTravelOverThePipe()
    {
        var payload = Encoding.UTF8.GetBytes("[]");
        var pipeName = $"SysMonitor-test-{Guid.NewGuid():N}";
        await using var pipe = ElevatedRegistryHelper.CreatePipe(pipeName);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var served = ElevatedRegistryHelper.ServeAsync(pipe, payload, cts.Token);
        var exitCode = await ElevatedRegistryHelper.ExecuteElevatedClean(pipeName, Fingerprint(payload));
        var result = await served;

        exitCode.Should().Be(0);
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Message.Should().Be("No issues to fix");
    }

    [Fact]
    public async Task AHelperThatNeverConnectsReportsNothingRatherThanSuccess()
    {
        await using var pipe = ElevatedRegistryHelper.CreatePipe($"SysMonitor-test-{Guid.NewGuid():N}");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        (await ElevatedRegistryHelper.ServeAsync(pipe, [], cts.Token)).Should().BeNull();
    }

    [Fact]
    public async Task AFrameOfAnImpossibleSizeIsRefused()
    {
        using var stream = new MemoryStream([0xFF, 0xFF, 0xFF, 0x7F]);

        var read = () => ElevatedRegistryHelper.ReadFrameAsync(stream, CancellationToken.None);

        await read.Should().ThrowAsync<IOException>();
    }

    [Fact]
    public async Task OnlyEntriesTheElevatedSidesOwnScanCallsBrokenAreCleaned()
    {
        var scanned = new RegistryIssue
        {
            Key = @"HKEY_LOCAL_MACHINE\SOFTWARE\SysMonitor.Test\Orphan",
            ValueName = "",
            Category = RegistryIssueCategory.OrphanedSoftware,
        };
        var cleaner = ScannerReturning(scanned);

        var approved = new[]
        {
            // the entry the scan agrees about, in the casing the registry treats as the same key
            new RegistryIssue { Key = scanned.Key.ToUpperInvariant(), ValueName = "", Category = scanned.Category },
            // an entry pointing somewhere else entirely
            new RegistryIssue { Key = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services", ValueName = "", Category = scanned.Category },
            // the same key, but asking for a different kind of deletion
            new RegistryIssue { Key = scanned.Key, ValueName = "", Category = RegistryIssueCategory.InvalidCOM },
            // the same key, but a value the scan never reported
            new RegistryIssue { Key = scanned.Key, ValueName = "Payload", Category = scanned.Category },
        };

        var confirmed = await ElevatedRegistryHelper.ConfirmedByOwnScanAsync(approved, cleaner);

        confirmed.Should().ContainSingle();
        confirmed[0].Key.Should().Be(scanned.Key.ToUpperInvariant());
        confirmed[0].IsSelected.Should().BeTrue();
    }

    [Fact]
    public async Task AnEmptyScanConfirmsNothing()
    {
        var cleaner = ScannerReturning();

        var confirmed = await ElevatedRegistryHelper.ConfirmedByOwnScanAsync(
            [new RegistryIssue { Key = @"HKEY_LOCAL_MACHINE\SOFTWARE\Anything", Category = RegistryIssueCategory.OrphanedSoftware }],
            cleaner);

        confirmed.Should().BeEmpty();
    }

    [Theory]
    [InlineData("Microsoft.WindowsCalculator_8wekyb3d8bbwe", "Microsoft.WindowsCalculator")]
    [InlineData("Microsoft.WindowsCalculator", "Microsoft.WindowsCalculator")]
    [InlineData("Some-App.Name2", "Some-App.Name2")]
    public void APackageNameThatIsOnlyANameIsUsed(string packageFullName, string expected)
    {
        var accepted = InstalledProgramsService.TryGetPackageName(
            new InstalledProgram { Name = "App", PackageFullName = packageFullName }, out var packageName, out _);

        accepted.Should().BeTrue();
        packageName.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("App'; Start-Process calc.exe; '")]
    [InlineData("App`nWrite-Output")]
    [InlineData("App$(hostname)")]
    [InlineData("App Name")]
    [InlineData("*")]
    public void APackageNameThatCouldCarryCommandsIsRefused(string packageFullName)
    {
        var accepted = InstalledProgramsService.TryGetPackageName(
            new InstalledProgram { Name = "App", PackageFullName = packageFullName }, out _, out var rejected);

        accepted.Should().BeFalse();
        rejected.Success.Should().BeFalse();
        rejected.Message.Should().Contain("will not pass to PowerShell");
    }

    [Fact]
    public void TheElevatedScriptTravelsInTheCommandLineAndNotInAFile()
    {
        const string script = "Get-AppxPackage -Name '*Microsoft.WindowsCalculator*' | Remove-AppxPackage";

        var arguments = InstalledProgramsService.BuildElevatedPowerShellArguments(script);

        arguments.Should().NotContain(".ps1", "a script file could be rewritten before the elevated run");
        arguments.Should().NotContain("-File");
        arguments.Should().Contain("-NoProfile").And.Contain("-EncodedCommand");

        var encoded = arguments[(arguments.IndexOf("-EncodedCommand", StringComparison.Ordinal) + "-EncodedCommand ".Length)..];
        Encoding.Unicode.GetString(Convert.FromBase64String(encoded)).Should().Be(script, "and it is the script that runs");
    }

    private static string Fingerprint(byte[] payload) => Convert.ToHexString(SHA256.HashData(payload));

    private static IRegistryCleaner ScannerReturning(params RegistryIssue[] issues)
    {
        var cleaner = new Mock<IRegistryCleaner>();
        cleaner.Setup(c => c.ScanAsync(It.IsAny<CancellationToken>())).ReturnsAsync(issues.ToList());
        return cleaner.Object;
    }
}
