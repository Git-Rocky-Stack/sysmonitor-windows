using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Core.Services.Utilities;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// A schedule is only worth anything if the run it starts cleans what the schedule says. The switches Task
/// Scheduler is given and the switches the app reads are the same code, and the run itself is checked here
/// against cleaners that report what they were asked to remove.
/// </summary>
public class ScheduledCleaningTests
{
    [Theory]
    [InlineData(true, true, true, true, true, true)]
    [InlineData(false, false, false, false, false, false)]
    [InlineData(true, false, true, false, true, false)]
    [InlineData(false, true, false, true, false, true)]
    public void WhatTheScheduleSaysIsWhatTheRunReads(bool temp, bool browser, bool recycle, bool update, bool thumbnails, bool notify)
    {
        var config = new ScheduledCleaningConfig
        {
            CleanTempFiles = temp,
            CleanBrowserCache = browser,
            CleanRecycleBin = recycle,
            CleanWindowsUpdateCache = update,
            CleanThumbnailCache = thumbnails,
            ShowNotification = notify,
        };
        var scheduled = ScheduledCleaningRequest.From(config);

        // The command line Task Scheduler stores, read back the way the app reads it.
        var commandLine = new[] { @"C:\Program Files\SysMonitor\SysMonitor.App.exe" }.Concat(scheduled.ToArguments()).ToList();
        var parsed = ScheduledCleaningRequest.FromCommandLine(commandLine);

        parsed.Should().Be(scheduled);
    }

    [Fact]
    public void AnOrdinaryStartIsNotAScheduledRun()
    {
        ScheduledCleaningRequest.FromCommandLine([@"C:\App\SysMonitor.App.exe"]).Should().BeNull();
        ScheduledCleaningRequest.FromCommandLine([@"C:\App\SysMonitor.App.exe", "--fix-registry", "in", "out", "hash"])
            .Should().BeNull("the app must still open normally for every other command line");
    }

    [Fact]
    public void TheSwitchIsEnoughToStartAScheduledRun()
    {
        var parsed = ScheduledCleaningRequest.FromCommandLine([@"C:\App\SysMonitor.App.exe", "--scheduled-clean", "--temp", "--silent"]);

        parsed.Should().NotBeNull();
        parsed!.TempFiles.Should().BeTrue();
        parsed.ShowNotification.Should().BeFalse();
        parsed.BrowserCache.Should().BeFalse();
        parsed.CleansAnything.Should().BeTrue();
    }

    [Fact]
    public void ARunWithNothingSelectedCleansNothing() =>
        ScheduledCleaningRequest.FromCommandLine([@"C:\App\SysMonitor.App.exe", "--scheduled-clean"])!
            .CleansAnything.Should().BeFalse();

    [Fact]
    public void EachSwitchStandsForTheRightKindOfRubbish()
    {
        new ScheduledCleaningRequest { TempFiles = true }.Categories
            .Should().BeEquivalentTo([CleanerCategory.WindowsTemp, CleanerCategory.UserTemp]);
        new ScheduledCleaningRequest { RecycleBin = true }.Categories.Should().BeEquivalentTo([CleanerCategory.RecycleBin]);
        new ScheduledCleaningRequest { WindowsUpdateCache = true }.Categories.Should().BeEquivalentTo([CleanerCategory.WindowsUpdateCache]);
        new ScheduledCleaningRequest { ThumbnailCache = true }.Categories.Should().BeEquivalentTo([CleanerCategory.Thumbnails]);
        new ScheduledCleaningRequest { BrowserCache = true }.Categories.Should().BeEmpty("browser caches have a cleaner of their own");
    }

    [Fact]
    public async Task ARunRemovesWhatWasAskedForAndNothingElse()
    {
        var found = new List<CleanerScanResult>
        {
            Item(CleanerCategory.UserTemp, 100, 10),
            Item(CleanerCategory.WindowsTemp, 200, 20),
            Item(CleanerCategory.Thumbnails, 400, 40),
            Item(CleanerCategory.LogFiles, 800, 80),          // never part of a schedule
            Item(CleanerCategory.WindowsUpdateCache, 1600, 160),
        };
        List<CleanerScanResult>? cleaned = null;

        var files = new Mock<ITempFileCleaner>();
        files.Setup(c => c.ScanAsync()).ReturnsAsync(found);
        files.Setup(c => c.CleanAsync(It.IsAny<IEnumerable<CleanerScanResult>>()))
            .Callback<IEnumerable<CleanerScanResult>>(items => cleaned = items.ToList())
            .ReturnsAsync(() => new CleanerResult { Success = true, BytesCleaned = 300, FilesDeleted = 30 });

        var browser = new Mock<IBrowserCacheCleaner>(MockBehavior.Strict);

        var outcome = await Runner(files.Object, browser.Object)
            .RunAsync(new ScheduledCleaningRequest { TempFiles = true, ShowNotification = false });

        cleaned.Should().NotBeNull();
        cleaned!.Select(i => i.Category).Should().BeEquivalentTo([CleanerCategory.UserTemp, CleanerCategory.WindowsTemp]);
        cleaned.Should().OnlyContain(i => i.IsSelected, "the cleaner acts on what is selected");
        outcome.BytesCleaned.Should().Be(300);
        outcome.FilesDeleted.Should().Be(30);
        outcome.Success.Should().BeTrue();
        browser.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ARunAddsUpWhatEveryCleanerRemoved()
    {
        var files = CleanerReturning(new CleanerResult { Success = true, BytesCleaned = 1000, FilesDeleted = 10 }, CleanerCategory.UserTemp);
        var browser = BrowserReturning(new CleanerResult { Success = true, BytesCleaned = 500, FilesDeleted = 5 });

        var outcome = await Runner(files, browser).RunAsync(new ScheduledCleaningRequest { TempFiles = true, BrowserCache = true });

        outcome.BytesCleaned.Should().Be(1500);
        outcome.FilesDeleted.Should().Be(15);
        outcome.Summary.Should().Contain("1.5 KB").And.Contain("15 files");
    }

    [Fact]
    public async Task WhatCouldNotBeRemovedIsReported()
    {
        var files = CleanerReturning(
            new CleanerResult { Success = false, BytesCleaned = 10, FilesDeleted = 1, Errors = ["C:\\locked.tmp: in use"] },
            CleanerCategory.UserTemp);

        var outcome = await Runner(files, Mock.Of<IBrowserCacheCleaner>()).RunAsync(new ScheduledCleaningRequest { TempFiles = true });

        outcome.Success.Should().BeFalse();
        outcome.Errors.Should().ContainSingle().Which.Should().Contain("locked.tmp");
        outcome.Summary.Should().Contain("could not be removed");
    }

    [Fact]
    public async Task OneCleanerFailingDoesNotStopTheOther()
    {
        var files = new Mock<ITempFileCleaner>();
        files.Setup(c => c.ScanAsync()).ThrowsAsync(new UnauthorizedAccessException("no access to the Windows temp folder"));
        var browser = BrowserReturning(new CleanerResult { Success = true, BytesCleaned = 700, FilesDeleted = 7 });

        var outcome = await Runner(files.Object, browser).RunAsync(new ScheduledCleaningRequest { TempFiles = true, BrowserCache = true });

        outcome.BytesCleaned.Should().Be(700, "the browser caches were still cleaned");
        outcome.Errors.Should().ContainSingle().Which.Should().Contain("no access");
        outcome.Success.Should().BeFalse();
    }

    [Fact]
    public async Task NothingIsScannedForAKindOfRubbishTheScheduleDidNotAskFor()
    {
        var files = new Mock<ITempFileCleaner>(MockBehavior.Strict);
        var browser = BrowserReturning(new CleanerResult { Success = true, BytesCleaned = 1, FilesDeleted = 1 });

        var outcome = await Runner(files.Object, browser).RunAsync(new ScheduledCleaningRequest { BrowserCache = true });

        outcome.Success.Should().BeTrue();
        files.VerifyNoOtherCalls();
    }

    private static ScheduledCleaningRunner Runner(ITempFileCleaner files, IBrowserCacheCleaner browser) =>
        new(files, browser, Mock.Of<ILogger<ScheduledCleaningRunner>>());

    private static CleanerScanResult Item(CleanerCategory category, long bytes, int files) => new()
    {
        Category = category,
        Name = category.ToString(),
        SizeBytes = bytes,
        FileCount = files,
        // A scan does not decide what a schedule cleans, and the cleaner skips whatever is not selected,
        // so the run has to select what it wants itself.
        IsSelected = false,
    };

    private static ITempFileCleaner CleanerReturning(CleanerResult result, params CleanerCategory[] categories)
    {
        var cleaner = new Mock<ITempFileCleaner>();
        cleaner.Setup(c => c.ScanAsync()).ReturnsAsync(categories.Select(c => Item(c, 1, 1)).ToList());
        cleaner.Setup(c => c.CleanAsync(It.IsAny<IEnumerable<CleanerScanResult>>())).ReturnsAsync(result);
        return cleaner.Object;
    }

    private static IBrowserCacheCleaner BrowserReturning(CleanerResult result)
    {
        var cleaner = new Mock<IBrowserCacheCleaner>();
        cleaner.Setup(c => c.ScanAsync()).ReturnsAsync([Item(CleanerCategory.BrowserCache, 1, 1)]);
        cleaner.Setup(c => c.CleanAsync(It.IsAny<IEnumerable<CleanerScanResult>>())).ReturnsAsync(result);
        return cleaner.Object;
    }
}
