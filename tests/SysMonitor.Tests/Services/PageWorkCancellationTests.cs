using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Work a page starts has to stop when the user leaves the page.
/// <para>
/// A registry scan walks tens of thousands of keys. Nothing could stop it: <c>ScanAsync</c> took no
/// cancellation token, and the view model that called it had none to give — so navigating away left the
/// scan running, still holding the page, its bindings and its results alive, and still writing into a
/// collection bound to a page the user had left.
/// </para>
/// </summary>
public class PageWorkCancellationTests
{
    private static RegistryCleaner Cleaner() => new(Mock.Of<ILogger<RegistryCleaner>>());

    [Fact]
    public async Task AScanTheUserHasAlreadyLeft_StopsInsteadOfFinishing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = async () => await Cleaner().ScanAsync(cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>(
            "a scan nobody is waiting for has to be able to stop");
    }

    [Fact]
    public async Task ACleanTheUserHasAlreadyLeft_StopsInsteadOfFinishing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var act = async () => await Cleaner().CleanAsync([], cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AScanNobodyCancelled_StillRuns()
    {
        var issues = await Cleaner().ScanAsync(CancellationToken.None);

        issues.Should().NotBeNull("the test above is worthless if the scan cannot run at all");
    }

    /// <summary>
    /// The three pages that start long work had no way to stop it. This reads the source because the test
    /// project cannot reference the WinUI layer.
    /// </summary>
    [Theory]
    [InlineData("RegistryCleanerViewModel")]
    [InlineData("DriveWiperViewModel")]
    [InlineData("BackupViewModel")]
    public void EveryViewModelThatStartsLongWork_CanBeToldToStop(string viewModel)
    {
        var source = File.ReadAllText(Path.Combine(
            RepoSource.Root, "src", "SysMonitor.App", "ViewModels", $"{viewModel}.cs"));

        source.Should().Contain("IDisposable",
            $"{viewModel} starts work the user can walk away from, and the page has to be able to end it");
        source.Should().Contain("CancellationTokenSource",
            $"{viewModel} needs something to cancel with");
        source.Should().Contain("public void Dispose()",
            $"{viewModel} has to release what it started");
    }
}
