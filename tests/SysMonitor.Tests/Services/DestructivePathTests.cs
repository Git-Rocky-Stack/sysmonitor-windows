using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SysMonitor.Core.Services.Backup;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The three places in this app that destroy data on purpose: the drive wiper, the scheduled cleaner, and
/// the backup password store. Each of these tests is about a case where the guard that was supposed to stop
/// something was not on the path that reaches it.
/// </summary>
public class DestructivePathTests : IDisposable
{
    private readonly TempDirectory _temp = new("destructive");

    public void Dispose() => _temp.Dispose();

    // ---------------------------------------------------------------- the wiper

    /// <summary>
    /// <c>SecureDeleteDirectoryAsync</c> refuses to wipe Windows, Program Files and the rest.
    /// <c>SecureDeleteFileAsync</c> did not: the check was only on the folder path. Pick
    /// <c>C:\Windows\System32\ntoskrnl.exe</c> in the file picker and the wiper would overwrite the kernel.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Windows\System32\ntoskrnl.exe")]
    [InlineData(@"C:\Windows\explorer.exe")]
    [InlineData(@"C:\Program Files\anything.dll")]
    public void AFileInAProtectedPlace_IsRefused(string path)
    {
        DriveWiper.IsProtectedLocation(path).Should().BeTrue(
            "the single-file wipe has to ask the same question the folder wipe asks");
    }

    [Theory]
    [InlineData(@"C:\Users\Someone\Documents\notes.txt")]
    [InlineData(@"D:\scratch\build.log")]
    public void AFileSomewhereOrdinary_IsNotRefused(string path)
    {
        DriveWiper.IsProtectedLocation(path).Should().BeFalse();
    }

    [Fact]
    public async Task WipingAFileInsideTheRealWindowsFolder_IsRefused()
    {
        var wiper = new DriveWiper(Mock.Of<ILogger<DriveWiper>>());

        // A path inside the machine's actual Windows folder, naming a file that is not there. The guard
        // runs before the existence check, so this exercises it without this test ever naming a file it
        // could destroy - which the assertion below makes sure of.
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SysMonitor-tests-this-file-does-not-exist.bin");
        File.Exists(path).Should().BeFalse("this test must never name a file it could wipe");

        var result = await wiper.SecureDeleteFileAsync(path, WipeMethod.SinglePass);

        result.Success.Should().BeFalse("a file inside a protected location is not the user's to wipe");
        result.ErrorMessage.Should().Contain("protected",
            "and the reason must be the protection, not that the file happened to be missing");
    }

    [Fact]
    public async Task WipingAnOrdinaryFile_StillWorks()
    {
        var wiper = new DriveWiper(Mock.Of<ILogger<DriveWiper>>());
        var path = Path.Combine(_temp.Path, "scratch.bin");
        await File.WriteAllTextAsync(path, "throwaway");

        var result = await wiper.SecureDeleteFileAsync(path, WipeMethod.SinglePass);

        result.Success.Should().BeTrue(result.ErrorMessage);
        File.Exists(path).Should().BeFalse("the test is worthless if the guard refuses everything");
    }

    // ---------------------------------------------------------------- the scheduled cleaner

    /// <summary>
    /// The trigger was <c>DateTime.Today.Add(timeOfDay)</c>. Set up a daily clean for 14:00 at four in the
    /// afternoon and the first trigger is two hours in the past; with "run missed schedules" on, Task
    /// Scheduler treats that as overdue and starts deleting within minutes of the user pressing Save.
    /// </summary>
    [Theory]
    [InlineData("2026-09-20T16:00:00", "14:00", "2026-09-21T14:00:00")]   // already past today
    [InlineData("2026-09-20T09:00:00", "14:00", "2026-09-20T14:00:00")]   // still to come today
    [InlineData("2026-09-20T14:00:00", "14:00", "2026-09-21T14:00:00")]   // exactly now counts as past
    public void TheFirstRunIsNeverInThePast(string nowText, string timeOfDayText, string expectedText)
    {
        var now = DateTime.Parse(nowText);
        var timeOfDay = TimeSpan.Parse(timeOfDayText);

        ScheduledCleaningService.FirstRunAfter(now, timeOfDay)
            .Should().Be(DateTime.Parse(expectedText),
                "a start time in the past is an overdue task, and this one deletes files");
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(32, 28)]
    [InlineData(99, 28)]
    [InlineData(1, 1)]
    [InlineData(28, 28)]
    [InlineData(31, 31)]
    public void TheDayOfMonthIsOneTaskSchedulerAccepts(int given, int expected)
    {
        // 29 to 31 are kept as asked - those days exist in most months. Anything outside 1 to 31 is
        // rejected by Task Scheduler outright, which leaves the user believing a schedule was created.
        ScheduledCleaningService.DayOfMonthWithin(given).Should().Be(expected);
    }

    // ---------------------------------------------------------------- the backup password store

    /// <summary>
    /// A saved schedule is a JSON file in the user's profile. Serialising the whole schedule wrote the
    /// backup's encryption password and any network share password into it in clear text — the password
    /// protecting the backup, sitting in a file next to it.
    /// </summary>
    [Fact]
    public void ASavedScheduleNeverCarriesAPassword()
    {
        var schedule = new BackupSchedule
        {
            Name = "Nightly",
            Job = new BackupJob
            {
                Name = "Nightly",
                EnableEncryption = true,
                EncryptionPassword = "correct-horse-battery-staple",
                NetworkPassword = "hunter2",
            },
        };

        var json = JsonSerializer.Serialize(schedule, new JsonSerializerOptions { WriteIndented = true });

        json.Should().NotContain("correct-horse-battery-staple");
        json.Should().NotContain("hunter2");
        json.Should().Contain("Nightly", "the rest of the schedule is still saved");
    }
}
