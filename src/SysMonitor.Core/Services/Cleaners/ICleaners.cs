using SysMonitor.Core.Models;

namespace SysMonitor.Core.Services.Cleaners;

public interface ITempFileCleaner
{
    Task<List<CleanerScanResult>> ScanAsync();
    Task<CleanerResult> CleanAsync(IEnumerable<CleanerScanResult> itemsToClean);
    Task<long> GetTotalCleanableBytesAsync();
}

public interface IBrowserCacheCleaner
{
    Task<List<CleanerScanResult>> ScanAsync();
    Task<CleanerResult> CleanAsync(IEnumerable<CleanerScanResult> itemsToClean);
}

public interface IRegistryCleaner
{
    Task<List<RegistryIssue>> ScanAsync();
    Task<CleanerResult> CleanAsync(IEnumerable<RegistryIssue> issuesToFix);
    Task<string> BackupRegistryAsync();
}
