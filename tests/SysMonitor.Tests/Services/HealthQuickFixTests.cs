using FluentAssertions;
using Moq;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Core.Services.Monitors;
using SysMonitor.Core.Services.Optimizers;
using SysMonitor.Core.Services.Utilities;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Quick Fix is the one-click path into the registry, so it keeps the Registry page's two promises: nothing changes
/// without a backup of every key it touches, and nothing is fixed that the user (or the scan, on the user's behalf)
/// left unselected.
/// </summary>
public class HealthQuickFixTests
{
    private readonly Mock<IRegistryCleaner> _registry = new();
    private readonly HealthCheckService _service;

    public HealthQuickFixTests()
    {
        _registry.Setup(r => r.CleanAsync(It.IsAny<IEnumerable<RegistryIssue>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CleanerResult { Success = true, FilesDeleted = 1 });

        _service = new HealthCheckService(
            Mock.Of<ITempFileCleaner>(), Mock.Of<IBrowserCacheCleaner>(), _registry.Object,
            Mock.Of<IStartupOptimizer>(), Mock.Of<IMemoryMonitor>(), Mock.Of<IDiskMonitor>());
    }

    [Fact]
    public async Task AFailedBackupLeavesTheRegistryAlone()
    {
        _registry.Setup(r => r.BackupRegistryAsync(It.IsAny<IEnumerable<RegistryIssue>>()))
            .ReturnsAsync(new RegistryBackupResult { Success = false, Message = "reg.exe failed" });

        var result = await _service.ApplyQuickFixAsync(Report(Issue(selected: true)));

        _registry.Verify(r => r.CleanAsync(It.IsAny<IEnumerable<RegistryIssue>>(), It.IsAny<CancellationToken>()), Times.Never);
        result.Errors.Should().Contain(error => error.Contains("reg.exe failed"));
        result.RegistryIssuesFixed.Should().Be(0);
    }

    [Fact]
    public async Task OnlySelectedIssuesAreFixedAndNoneIsSelectedForTheUser()
    {
        _registry.Setup(r => r.BackupRegistryAsync(It.IsAny<IEnumerable<RegistryIssue>>()))
            .ReturnsAsync(new RegistryBackupResult { Success = true, BackupPath = @"C:\backup.reg" });
        var kept = Issue(selected: true);
        var leftAlone = Issue(selected: false);

        await _service.ApplyQuickFixAsync(Report(kept, leftAlone));

        leftAlone.IsSelected.Should().BeFalse("Quick Fix must not widen the selection");
        _registry.Verify(r => r.CleanAsync(
            It.Is<IEnumerable<RegistryIssue>>(issues => issues.Single() == kept), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static HealthCheckReport Report(params RegistryIssue[] issues) => new() { RegistryIssues = issues.ToList() };

    private static RegistryIssue Issue(bool selected) => new()
    {
        Key = @"HKEY_CURRENT_USER\Software\Example",
        ValueName = "Value",
        RiskLevel = CleanerRiskLevel.Safe,
        IsSelected = selected,
    };
}
