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
    /// <param name="cancellationToken">
    /// Stops the scan. It walks tens of thousands of keys, and a user who has left the page is not waiting
    /// for the answer - without this there was no way to tell it so.
    /// </param>
    Task<List<RegistryIssue>> ScanAsync(CancellationToken cancellationToken = default);

    Task<CleanerResult> CleanAsync(IEnumerable<RegistryIssue> issuesToFix, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports every registry key that fixing <paramref name="issuesToFix"/> would modify into one .reg file.
    /// Fails (and writes nothing) when an existing key cannot be exported.
    /// </summary>
    Task<RegistryBackupResult> BackupRegistryAsync(IEnumerable<RegistryIssue> issuesToFix);

    /// <summary>Imports a backup written by <see cref="BackupRegistryAsync"/>; asks for elevation when it holds machine-wide keys.</summary>
    Task<RegistryRestoreResult> RestoreRegistryBackupAsync(string backupPath);

    /// <summary>Folder holding registry backups.</summary>
    string BackupFolder { get; }

    /// <summary>Registry backup files, newest first.</summary>
    IReadOnlyList<string> GetBackups();
}
