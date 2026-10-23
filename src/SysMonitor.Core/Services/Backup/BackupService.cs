using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using SysMonitor.Core.Helpers;
using SysMonitor.Core.Services.Cleaners;
using SysMonitor.Core.Services.Utilities;

namespace SysMonitor.Core.Services.Backup;

/// <summary>
/// Comprehensive backup service implementation for Windows
/// </summary>
public class BackupService : IBackupService
{
    private bool _disposed;

    private readonly ILogger _logger;
    private readonly ISystemRestoreService _systemRestore;

    private readonly string _backupMetadataFolder;
    private readonly string _manifestFileName = "backup_manifest.json";
    private bool _isBackupInProgress;
    private CancellationTokenSource? _currentBackupCts;

    public bool IsBackupInProgress => _isBackupInProgress;

    public BackupService(ILogger<BackupService>? logger = null, ISystemRestoreService? systemRestore = null)
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "Backups"), logger, systemRestore)
    {
    }

    /// <summary>Uses <paramref name="metadataFolder"/> for the backup catalog (tests use an isolated folder).</summary>
    internal BackupService(string metadataFolder, ILogger<BackupService>? logger = null,
        ISystemRestoreService? systemRestore = null)
    {
        _logger = logger ?? NullLogger<BackupService>.Instance;
        _systemRestore = systemRestore ?? new SystemRestoreService();
        _backupMetadataFolder = metadataFolder;
        Directory.CreateDirectory(_backupMetadataFolder);
    }

    // ==================== MAIN BACKUP OPERATION ====================

    public async Task<BackupResult> CreateBackupAsync(BackupJob job, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (_isBackupInProgress)
        {
            return new BackupResult
            {
                Success = false,
                Status = BackupStatus.Failed,
                Message = "A backup is already in progress"
            };
        }

        _isBackupInProgress = true;
        _currentBackupCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var startTime = DateTime.Now;
        var errors = new List<BackupError>();
        var processedFiles = 0;
        var skippedFiles = 0;
        var failedFiles = 0;
        long processedBytes = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Validate job
            if (job.SourcePaths.Count == 0)
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = "No source paths specified"
                };
            }

            if (job.EnableEncryption && string.IsNullOrEmpty(job.EncryptionPassword))
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = "Encryption is enabled but no password was provided"
                };
            }

            var backupFolderName = $"Backup_{job.Name}_{DateTime.Now:yyyyMMdd_HHmmss}";
            var backupPath = Path.Combine(job.DestinationPath, backupFolderName);

            // Collect files to backup
            progress?.Report(new BackupProgress
            {
                Status = BackupStatus.Running,
                CurrentOperation = "Scanning files...",
                CurrentFile = ""
            });

            var sourceFiles = await CollectFilesAsync(job, _currentBackupCts.Token);
            var filesToBackup = sourceFiles;

            if (sourceFiles.Count == 0)
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = "No files found to backup"
                };
            }

            // An incremental copies only what is new or changed since the backup it builds on - the newest of this
            // name, full or incremental - so each one holds the difference from the one before it.
            BackupArchive? parent = null;
            Dictionary<string, BackupCatalogEntry>? parentCatalog = null;
            var startedNewChain = "";
            if (job.Type == BackupType.Incremental)
            {
                progress?.Report(new BackupProgress
                {
                    Status = BackupStatus.Running,
                    CurrentOperation = "Finding the backup this one builds on...",
                    CurrentFile = ""
                });

                (parent, startedNewChain) = await FindParentAsync(job, _currentBackupCts.Token);
                if (parent != null)
                {
                    parentCatalog = CatalogByPath(CatalogOf(parent.Manifest!));
                    filesToBackup = sourceFiles.Where(f => IsNewOrChanged(f, parentCatalog)).ToList();

                    if (filesToBackup.Count == 0 && CatalogOf(parent.Manifest!).Count == sourceFiles.Count)
                    {
                        return new BackupResult
                        {
                            Success = true,
                            Status = BackupStatus.Completed,
                            Message = $"Nothing has changed since the backup of {parent.CreatedDate:g}, so no new backup was needed",
                            TotalFiles = sourceFiles.Count,
                            Duration = stopwatch.Elapsed,
                            StartTime = startTime,
                            EndTime = DateTime.Now
                        };
                    }
                }
            }

            var totalFiles = filesToBackup.Count;
            var totalBytes = filesToBackup.Sum(f => f.Length);
            Directory.CreateDirectory(backupPath);

            // Create manifest
            var manifest = new BackupManifest
            {
                BackupId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                Files = [],
                ParentBackupId = parent?.Id,
                SourceRoots = job.SourcePaths.ToList(),
                Metadata = new Dictionary<string, string>
                {
                    ["BackupType"] = job.Type.ToString(),
                    ["ComputerName"] = Environment.MachineName,
                    ["UserName"] = Environment.UserName,
                    ["BackupName"] = job.Name
                }
            };

            // Each source gets a folder of its own in the backup, named once, before any file is copied.
            var sourceFolders = SourceFolderNames(job.SourcePaths);

            // Backup files
            var lastProgressUpdate = DateTime.Now;
            var bytesSinceLastUpdate = 0L;
            var notCopied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in filesToBackup)
            {
                _currentBackupCts.Token.ThrowIfCancellationRequested();

                try
                {
                    // The source this file came from: the deepest one that holds it, matched on whole folders, so
                    // a file in D:\TempProjects is not taken to be inside a source D:\Temp.
                    var sourcePath = job.SourcePaths
                        .Where(source => string.Equals(source, file.FullName, StringComparison.OrdinalIgnoreCase)
                                         || PathHelper.IsPathWithinDirectory(file.FullName, source))
                        .OrderByDescending(source => source.Length)
                        .FirstOrDefault();
                    var relativePath = sourcePath != null && !string.Equals(sourcePath, file.FullName, StringComparison.OrdinalIgnoreCase)
                        ? Path.GetRelativePath(sourcePath, file.FullName)
                        : file.Name;

                    // Preserve folder structure
                    var sourceRootName = sourcePath != null ? sourceFolders[sourcePath] : "Files";
                    var destFilePath = Path.Combine(backupPath, sourceRootName, relativePath);
                    var destDir = Path.GetDirectoryName(destFilePath);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    // Copy file
                    await CopyFileWithProgressAsync(file.FullName, destFilePath, _currentBackupCts.Token);

                    // Calculate hash for verification
                    var hash = job.VerifyAfterBackup ? await CalculateFileHashAsync(destFilePath) : "";

                    // Add to manifest
                    manifest.Files.Add(new BackupFileEntry
                    {
                        RelativePath = Path.Combine(sourceRootName, relativePath),
                        OriginalPath = file.FullName,
                        SizeBytes = file.Length,
                        ModifiedDate = file.LastWriteTime,
                        Hash = hash,
                        Attributes = file.Attributes
                    });

                    processedFiles++;
                    processedBytes += file.Length;
                    bytesSinceLastUpdate += file.Length;

                    // Report progress (throttled)
                    if ((DateTime.Now - lastProgressUpdate).TotalMilliseconds > 100)
                    {
                        var elapsed = stopwatch.Elapsed;
                        var bytesPerSecond = elapsed.TotalSeconds > 0 ? (long)(processedBytes / elapsed.TotalSeconds) : 0;
                        var remainingBytes = totalBytes - processedBytes;
                        var eta = bytesPerSecond > 0 ? TimeSpan.FromSeconds(remainingBytes / bytesPerSecond) : (TimeSpan?)null;

                        progress?.Report(new BackupProgress
                        {
                            Status = BackupStatus.Running,
                            CurrentFile = file.Name,
                            CurrentOperation = "Copying files...",
                            TotalBytes = totalBytes,
                            ProcessedBytes = processedBytes,
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                            Elapsed = elapsed,
                            EstimatedTimeRemaining = eta,
                            BytesPerSecond = bytesPerSecond
                        });

                        lastProgressUpdate = DateTime.Now;
                        bytesSinceLastUpdate = 0;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failedFiles++;
                    notCopied.Add(file.FullName);
                    errors.Add(new BackupError
                    {
                        FilePath = file.FullName,
                        ErrorMessage = ex.Message
                    });
                }
            }

            manifest.Catalog = BuildCatalog(sourceFiles, notCopied, parentCatalog);

            // Save manifest
            var manifestPath = Path.Combine(backupPath, _manifestFileName);
            await SaveManifestAsync(manifest, manifestPath);

            // Apply compression if requested
            string finalBackupPath = backupPath;
            if (job.Compression != BackupCompression.None)
            {
                progress?.Report(new BackupProgress
                {
                    Status = BackupStatus.Running,
                    CurrentOperation = "Compressing backup...",
                    ProcessedBytes = processedBytes,
                    TotalBytes = totalBytes,
                    ProcessedFiles = processedFiles,
                    TotalFiles = totalFiles
                });

                var zipPath = backupPath + ".zip";
                await CompressBackupAsync(backupPath, zipPath, job.Compression, _currentBackupCts.Token);

                // Delete uncompressed folder
                Directory.Delete(backupPath, true);
                finalBackupPath = zipPath;
            }

            // Apply encryption if requested
            var isEncrypted = false;
            if (job.EnableEncryption && !string.IsNullOrEmpty(job.EncryptionPassword))
            {
                progress?.Report(new BackupProgress
                {
                    Status = BackupStatus.Running,
                    CurrentOperation = "Encrypting backup...",
                    ProcessedBytes = processedBytes,
                    TotalBytes = totalBytes,
                    ProcessedFiles = processedFiles,
                    TotalFiles = totalFiles
                });

                // Encryption works on a single file, so an uncompressed backup folder is packed into a zip first.
                if (Directory.Exists(finalBackupPath))
                {
                    var packedPath = finalBackupPath + ".zip";
                    await CompressBackupAsync(finalBackupPath, packedPath, BackupCompression.None, _currentBackupCts.Token);
                    Directory.Delete(finalBackupPath, true);
                    finalBackupPath = packedPath;
                }

                var encryptedPath = finalBackupPath + ".enc";
                try
                {
                    await BackupEncryption.EncryptFileAsync(finalBackupPath, encryptedPath, job.EncryptionPassword, _currentBackupCts.Token);
                }
                catch
                {
                    // The user asked for an encrypted backup, so a failed encryption must not leave the plain copy
                    // behind at the destination - a USB stick or a synced folder - nor half a .enc beside it.
                    TryDeleteFile(finalBackupPath);
                    TryDeleteFile(encryptedPath);
                    throw;
                }
                File.Delete(finalBackupPath);
                finalBackupPath = encryptedPath;
                isEncrypted = true;
            }

            stopwatch.Stop();

            // Create archive record
            var archive = new BackupArchive
            {
                Id = manifest.BackupId,
                Name = job.Name,
                FilePath = finalBackupPath,
                Type = job.Type,
                CreatedDate = DateTime.Now,
                SizeBytes = GetBackupSize(finalBackupPath),
                FileCount = processedFiles,
                IsEncrypted = isEncrypted,
                IsVerified = false,
                Description = job.Description,
                SourcePaths = job.SourcePaths,
                ParentBackupId = parent?.Id,
                Manifest = manifest
            };

            // Verify the finished backup (after compression/encryption) against the recorded checksums.
            BackupResult? verification = null;
            if (job.VerifyAfterBackup)
            {
                verification = await VerifyBackupAsync(archive, progress, job.EncryptionPassword, _currentBackupCts.Token);
                archive.IsVerified = verification.Success;
            }

            // Save archive metadata
            await SaveArchiveMetadataAsync(archive);

            if (verification is { Success: false })
            {
                // Keep older backups: retention cleanup only runs after a backup that verified.
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = $"Backup was written to {finalBackupPath}, but verification failed: {verification.Message}",
                    OutputPath = finalBackupPath,
                    TotalBytes = totalBytes,
                    ProcessedBytes = processedBytes,
                    TotalFiles = totalFiles,
                    ProcessedFiles = processedFiles,
                    FailedFiles = failedFiles,
                    Duration = stopwatch.Elapsed,
                    StartTime = startTime,
                    EndTime = DateTime.Now,
                    Errors = [.. errors, .. verification.Errors],
                    Archive = archive
                };
            }

            // Old backups are pruned only after a complete one: a run that could not copy every file is not a
            // replacement for the backup retention would delete to make room for it.
            if (failedFiles == 0)
            {
                await CleanupOldBackupsAsync(job);
            }

            var status = failedFiles > 0 ? BackupStatus.PartialSuccess : BackupStatus.Completed;

            // An incremental says what it holds and what it builds on, or why it had nothing to build on.
            var kind = job.Type != BackupType.Incremental
                ? ""
                : parent != null
                    ? $": {processedFiles} new or changed file(s) since the backup of {parent.CreatedDate:g}"
                    : $": {startedNewChain}, so this one holds every file";

            return new BackupResult
            {
                Success = true,
                Status = status,
                Message = failedFiles > 0
                    ? $"Backup completed with {failedFiles} errors{kind}"
                    : $"Backup completed successfully{kind}",
                OutputPath = finalBackupPath,
                TotalBytes = totalBytes,
                ProcessedBytes = processedBytes,
                TotalFiles = totalFiles,
                ProcessedFiles = processedFiles,
                SkippedFiles = skippedFiles,
                FailedFiles = failedFiles,
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTime.Now,
                Errors = errors,
                Archive = archive
            };
        }
        catch (OperationCanceledException)
        {
            return new BackupResult
            {
                Success = false,
                Status = BackupStatus.Cancelled,
                Message = "Backup was cancelled",
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTime.Now
            };
        }
        catch (Exception ex)
        {
            return new BackupResult
            {
                Success = false,
                Status = BackupStatus.Failed,
                Message = $"Backup failed: {ex.Message}",
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTime.Now,
                Errors = [new BackupError { ErrorMessage = ex.Message }]
            };
        }
        finally
        {
            _isBackupInProgress = false;
            _currentBackupCts?.Dispose();
            _currentBackupCts = null;
        }
    }

    /// <summary>
    /// Cancels a backup that is still running and releases its token source. The host calls this at
    /// shutdown; without it a copy loop carried on writing after the window had closed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _currentBackupCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Best effort: the backup finished and disposed its own token source first.
        }

        _currentBackupCts?.Dispose();
        _currentBackupCts = null;

        GC.SuppressFinalize(this);
    }

    // ==================== SYSTEM IMAGE BACKUP ====================

    /// <summary>The drive facts a system image is planned from; tests stand in drives that are not there.</summary>
    internal Func<string, DriveFacts?> InspectDrive { get; set; } = SystemImage.InspectDrive;

    /// <summary>Runs a program as administrator with its output in a file; tests stand in Windows Backup.</summary>
    internal Func<string, string, string, Task<int>> RunElevatedAsync { get; set; } = SystemImage.RunElevatedAsync;

    /// <summary>Windows' own folder; the system drive is the one it is on.</summary>
    internal string SystemDirectory { get; set; } = Environment.SystemDirectory;

    private string LogFolder => Path.Combine(Path.GetDirectoryName(_backupMetadataFolder) ?? _backupMetadataFolder, "Logs");

    public Task<SystemImagePlan> PrepareSystemImageAsync(string destinationPath) =>
        Task.Run(() => SystemImage.Plan(destinationPath, SystemDirectory, InspectDrive));

    /// <summary>
    /// Makes a system image with Windows Backup: the system drive and the partitions Windows starts from, which
    /// Windows Recovery's System Image Recovery can put back onto this disk or a new one. wbadmin needs
    /// administrator rights, so Windows asks for them; its report is read as it is written, for progress, and
    /// kept in the log folder. Cancelling asks Windows Backup to stop, which needs the same permission again.
    /// </summary>
    public async Task<BackupResult> CreateSystemImageAsync(string destinationPath, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();

        BackupResult Finish(bool success, BackupStatus status, string message, string? outputPath = null, BackupArchive? archive = null) => new()
        {
            Success = success,
            Status = status,
            Message = message,
            OutputPath = outputPath,
            Duration = stopwatch.Elapsed,
            StartTime = startTime,
            EndTime = DateTime.Now,
            Archive = archive
        };

        if (_isBackupInProgress)
            return Finish(false, BackupStatus.Failed, "A backup is already in progress");

        var plan = await PrepareSystemImageAsync(destinationPath);
        if (plan.Problem != null)
            return Finish(false, BackupStatus.Failed, plan.Problem);

        _isBackupInProgress = true;
        try
        {
            Directory.CreateDirectory(LogFolder);
            var logPath = Path.Combine(LogFolder, $"{SystemImage.LogPrefix}{DateTime.Now:yyyyMMdd-HHmmss}.log");

            progress?.Report(new BackupProgress
            {
                Status = BackupStatus.Running,
                CurrentOperation = "Waiting for administrator permission...",
                CurrentFile = "Windows Backup needs it to read the whole drive"
            });

            Task<int> run;
            try
            {
                run = RunElevatedAsync(SystemImage.WbadminPath(), SystemImage.Arguments(plan), logPath);
            }
            catch (Exception ex)
            {
                // A refusal can come before there is a task to await; it is handled below with the rest.
                run = Task.FromException<int>(ex);
            }
            var stopping = false;
            while (!run.IsCompleted)
            {
                await Task.WhenAny(run, Task.Delay(1000, CancellationToken.None));

                if (cancellationToken.IsCancellationRequested && !stopping)
                {
                    stopping = true;
                    _ = StopWindowsBackupAsync();
                }

                var report = SystemImage.ReadLog(logPath);
                var percent = SystemImage.LastPercent(report) ?? 0;
                progress?.Report(new BackupProgress
                {
                    Status = BackupStatus.Running,
                    CurrentOperation = stopping ? "Stopping Windows Backup..." : "Creating system image...",
                    CurrentFile = SystemImage.LastLine(report) ?? "Starting Windows Backup...",
                    TotalBytes = 100,
                    ProcessedBytes = percent,
                    Elapsed = stopwatch.Elapsed
                });
            }

            int exitCode;
            try
            {
                exitCode = await run;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                return Finish(false, BackupStatus.Cancelled,
                    "Windows did not get administrator permission, so no system image was made.");
            }

            var log = SystemImage.ReadLog(logPath);
            stopwatch.Stop();

            if (stopping)
                return Finish(false, BackupStatus.Cancelled,
                    $"The system image was cancelled. Windows keeps only complete images, so nothing usable was added. Windows Backup's report is in {logPath}.");

            if (exitCode != 0)
                return Finish(false, BackupStatus.Failed,
                    $"No system image was made: {SystemImage.FailureLine(log, exitCode)} Windows Backup's report is in {logPath}.");

            var archive = await RecordSystemImageAsync(plan);
            return Finish(true, BackupStatus.Completed,
                $"System image of {plan.SystemDrive} saved in {plan.ImageFolder}", plan.ImageFolder, archive);
        }
        catch (Exception ex)
        {
            return Finish(false, BackupStatus.Failed, $"System image error: {ex.Message}");
        }
        finally
        {
            _isBackupInProgress = false;
        }
    }

    /// <summary>Asks Windows Backup to stop the image it is writing; Windows asks for permission again.</summary>
    private async Task StopWindowsBackupAsync()
    {
        try
        {
            var logPath = Path.Combine(LogFolder, $"{SystemImage.LogPrefix}stop-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            await RunElevatedAsync(SystemImage.WbadminPath(), "stop job -quiet", logPath);
        }
        catch (Exception ex)
        {
            // The person may refuse the second prompt; the image then runs to its end and is reported as cancelled.
            _logger.LogDebug(ex, "StopWindowsBackupAsync failed");
        }
    }

    /// <summary>
    /// Puts the image in the history. Windows keeps every version of a target's images in one WindowsImageBackup
    /// folder, so one entry stands for that folder: it replaces the earlier entry, and its size grows by what this
    /// run added (the folder itself is closed to ordinary accounts, so the free space is what can be read).
    /// </summary>
    private async Task<BackupArchive> RecordSystemImageAsync(SystemImagePlan plan)
    {
        var earlier = (await LoadArchivesAsync(onlyPresent: false))
            .Where(a => a.Type == BackupType.SystemImage && string.Equals(a.FilePath, plan.ImageFolder, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var added = 0L;
        if (!plan.IsNetworkTarget && InspectDrive(plan.Target + "\\") is { IsReady: true } after)
            added = Math.Max(0, plan.TargetFreeBytes - after.FreeBytes);

        var archive = new BackupArchive
        {
            Name = $"System image of {plan.SystemDrive}",
            FilePath = plan.ImageFolder,
            Type = BackupType.SystemImage,
            CreatedDate = DateTime.Now,
            SizeBytes = earlier.Select(a => a.SizeBytes).DefaultIfEmpty(0).Max() + added,
            IsVerified = false,
            Description = SystemImage.RestoreInstructions,
            SourcePaths = [plan.SystemDrive + "\\"]
        };

        foreach (var old in earlier)
            TryDeleteFile(Path.Combine(_backupMetadataFolder, $"{old.Id}.json"));
        await SaveArchiveMetadataAsync(archive);
        return archive;
    }

    // ==================== RESTORE POINT ====================

    /// <summary>
    /// Creates a system restore point.
    /// <para>
    /// This used to run <c>powershell -Command "Checkpoint-Computer -Description '{description}'"</c>. A
    /// description containing an apostrophe closed that literal and the rest of it was another statement -
    /// and the <c>Verb = "runas"</c> beside it did nothing, because a verb is only honoured when the shell
    /// starts the process. The Health Check page has always used <see cref="ISystemRestoreService"/>, which
    /// passes the description as a typed WMI parameter that no parser ever sees. Both pages now do.
    /// </para>
    /// </summary>
    public async Task<BackupResult> CreateRestorePointAsync(string description)
    {
        var result = await _systemRestore.CreateRestorePointAsync(description, RestorePointType.ModifySettings);

        return new BackupResult
        {
            Success = result.Success,
            Status = result.Success ? BackupStatus.Completed : BackupStatus.Failed,
            Message = result.Message
        };
    }

    /// <summary>
    /// The restore points, read through the System Restore service's WMI query. Parsing PowerShell's JSON here
    /// never worked: CreationTime comes back in WMI's <c>20240101120000.000000-000</c> form, which DateTime.Parse
    /// rejects, and RestorePointType is a number, so every call threw and the page always showed none.
    /// </summary>
    public async Task<List<RestorePointInfo>> GetRestorePointsAsync()
    {
        try
        {
            var points = await _systemRestore.GetRestorePointsAsync();
            return points.Select(point => new RestorePointInfo
            {
                SequenceNumber = (int)point.SequenceNumber,
                Description = point.Description,
                CreationTime = point.CreationTime,
                RestorePointType = point.Type.ToString(),
            }).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetRestorePointsAsync failed");
            return [];
        }
    }

    // ==================== RESTORE OPERATIONS ====================

    /// <summary>
    /// Restores a backup. An incremental holds only what changed, so it is rebuilt from its chain: each file the
    /// incremental stands for comes from the newest backup in the chain that holds it, and nothing is written twice.
    /// </summary>
    public async Task<BackupResult> RestoreBackupAsync(BackupArchive archive, string destinationPath, RestoreOptions options, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (archive.Type == BackupType.SystemImage)
            return new BackupResult { Success = false, Status = BackupStatus.Failed, Message = SystemImage.RestoreInstructions };

        if (archive.ParentBackupId == null)
            return await RestoreLayerAsync(archive, destinationPath, options, progress, cancellationToken);

        var startTime = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();

        BackupResult Finish(bool success, BackupStatus status, string message, int processed = 0, int total = 0, List<BackupError>? errors = null) => new()
        {
            Success = success,
            Status = status,
            Message = message,
            ProcessedFiles = processed,
            TotalFiles = total,
            Duration = stopwatch.Elapsed,
            StartTime = startTime,
            EndTime = DateTime.Now,
            Errors = errors ?? []
        };

        var chain = await ChainForAsync(archive);
        if (chain == null || chain.Any(a => a.Manifest == null))
            return Finish(false, BackupStatus.Failed, MissingChainMessage(archive));

        var layers = AssignLayers(chain.Select(a => (a, a.Manifest!)).ToList(), options.SelectiveFiles, out var unheld);
        var errors = new List<BackupError>();
        if (unheld > 0)
            errors.Add(new BackupError { FilePath = archive.FilePath, ErrorMessage = $"{unheld} file(s) are in no backup of this chain" });

        var processed = 0;
        var total = 0;
        for (var i = 0; i < layers.Count; i++)
        {
            var (layer, entries) = layers[i];
            if (entries.Count == 0)
                continue;

            var layerOptions = new RestoreOptions
            {
                OverwriteExisting = options.OverwriteExisting,
                PreservePermissions = options.PreservePermissions,
                RestoreToOriginalLocation = options.RestoreToOriginalLocation,
                AlternateDestination = options.AlternateDestination,
                SelectiveFiles = entries.Select(e => e.RelativePath).ToList(),
                VerifyAfterRestore = options.VerifyAfterRestore,
                Password = options.Password
            };
            var layerProgress = progress == null ? null : new LayerProgress(progress, i + 1, layers.Count);

            var result = await RestoreLayerAsync(layer, destinationPath, layerOptions, layerProgress, cancellationToken);
            if (!result.Success && result.ProcessedFiles == 0 && result.Errors.Count == 0)
            {
                // The layer could not be opened at all - a wrong password, a damaged archive: say which one.
                return Finish(false, BackupStatus.Failed,
                    $"The backup of {layer.CreatedDate:g} in this chain could not be read: {result.Message}", processed, total, errors);
            }

            processed += result.ProcessedFiles;
            total += result.TotalFiles;
            errors.AddRange(result.Errors);
        }

        var restoredNothing = processed == 0 && errors.Count > 0;
        return Finish(!restoredNothing,
            restoredNothing ? BackupStatus.Failed : errors.Count > 0 ? BackupStatus.PartialSuccess : BackupStatus.Completed,
            errors.Count > 0
                ? $"Restored {processed} file(s) from {chain.Count} backups in the chain, with {errors.Count} errors"
                : $"Restore completed successfully: {processed} file(s), rebuilt from {chain.Count} backups in the chain",
            processed, total, errors);
    }

    /// <summary>Reports a chain restore's progress with which backup of the chain it is reading.</summary>
    private sealed class LayerProgress(IProgress<BackupProgress> inner, int layer, int layers) : IProgress<BackupProgress>
    {
        public void Report(BackupProgress value) =>
            inner.Report(value with { CurrentOperation = $"{value.CurrentOperation} (backup {layer} of {layers})" });
    }

    /// <summary>Restores what one backup holds, without looking at any backup it builds on.</summary>
    private async Task<BackupResult> RestoreLayerAsync(BackupArchive archive, string destinationPath, RestoreOptions options, IProgress<BackupProgress>? progress, CancellationToken cancellationToken)
    {
        var startTime = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();
        var processedFiles = 0;
        var errors = new List<BackupError>();
        string? decryptedArchivePath = null;
        string? tempExtractPath = null;

        try
        {
            var sourcePath = archive.FilePath;

            // Decrypt if needed
            if (archive.IsEncrypted || sourcePath.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(options.Password))
                {
                    return new BackupResult
                    {
                        Success = false,
                        Status = BackupStatus.Failed,
                        Message = "This backup is encrypted. Enter its password to restore it."
                    };
                }

                progress?.Report(new BackupProgress
                {
                    Status = BackupStatus.Running,
                    CurrentOperation = "Decrypting backup..."
                });

                decryptedArchivePath = Path.Combine(Path.GetTempPath(), $"restore_{Guid.NewGuid():N}.zip");
                try
                {
                    await BackupEncryption.DecryptFileAsync(sourcePath, decryptedArchivePath, options.Password, cancellationToken);
                }
                catch (BackupPasswordException ex)
                {
                    return new BackupResult { Success = false, Status = BackupStatus.Failed, Message = ex.Message };
                }

                sourcePath = decryptedArchivePath;
            }

            // Extract if compressed
            string extractPath;
            if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                tempExtractPath = Path.Combine(Path.GetTempPath(), $"restore_{Guid.NewGuid():N}");
                extractPath = tempExtractPath;
                progress?.Report(new BackupProgress
                {
                    Status = BackupStatus.Running,
                    CurrentOperation = "Extracting backup archive..."
                });

                try
                {
                    ZipFile.ExtractToDirectory(sourcePath, extractPath);
                }
                catch (InvalidDataException) when (decryptedArchivePath != null)
                {
                    // Legacy encrypted backups have no authentication tag; a wrong password can
                    // decrypt to bytes that are not a valid archive.
                    return new BackupResult
                    {
                        Success = false,
                        Status = BackupStatus.Failed,
                        Message = "Incorrect password, or the backup file has been altered or damaged."
                    };
                }
            }
            else
            {
                extractPath = sourcePath;
            }

            // Load manifest
            var manifestPath = Path.Combine(extractPath, _manifestFileName);
            if (!File.Exists(manifestPath))
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = "Backup manifest not found"
                };
            }

            var manifest = await LoadManifestAsync(manifestPath);
            var selective = options.SelectiveFiles?.ToHashSet(StringComparer.Ordinal);
            var filesToRestore = selective != null
                ? manifest.Files.Where(f => selective.Contains(f.RelativePath)).ToList()
                : manifest.Files;

            var plan = PlanRestore(manifest, filesToRestore, extractPath, destinationPath, options);
            errors.AddRange(plan.Refused);
            var totalFiles = plan.Files.Count;

            foreach (var planned in plan.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileEntry = planned.Entry;

                try
                {
                    var sourceFile = planned.SourceFile;
                    var destFile = planned.DestinationFile;

                    var destDir = Path.GetDirectoryName(destFile);
                    if (!string.IsNullOrEmpty(destDir))
                    {
                        Directory.CreateDirectory(destDir);
                    }

                    if (File.Exists(destFile) && !options.OverwriteExisting)
                    {
                        continue;
                    }

                    File.Copy(sourceFile, destFile, options.OverwriteExisting);

                    if (options.PreservePermissions)
                    {
                        File.SetAttributes(destFile, fileEntry.Attributes);
                        File.SetLastWriteTime(destFile, fileEntry.ModifiedDate);
                    }

                    processedFiles++;

                    progress?.Report(new BackupProgress
                    {
                        Status = BackupStatus.Running,
                        CurrentFile = fileEntry.RelativePath,
                        CurrentOperation = "Restoring files...",
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles
                    });
                }
                catch (Exception ex)
                {
                    errors.Add(new BackupError
                    {
                        FilePath = fileEntry.OriginalPath,
                        ErrorMessage = ex.Message
                    });
                }
            }

            stopwatch.Stop();

            // A restore that wrote nothing of what there was to write did not succeed, whatever the reason.
            var restoredNothing = processedFiles == 0 && (errors.Count > 0 || plan.Refused.Count > 0);

            return new BackupResult
            {
                Success = !restoredNothing,
                Status = restoredNothing ? BackupStatus.Failed : errors.Count > 0 ? BackupStatus.PartialSuccess : BackupStatus.Completed,
                Message = plan.Refused.Count > 0
                    ? $"Restored {processedFiles} file(s); {plan.Refused.Count} entry(ies) in the backup asked to be written outside the folders it was taken from and were refused"
                    : errors.Count > 0 ? $"Restored with {errors.Count} errors" : "Restore completed successfully",
                ProcessedFiles = processedFiles,
                TotalFiles = totalFiles,
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTime.Now,
                Errors = errors
            };
        }
        catch (Exception ex)
        {
            return new BackupResult
            {
                Success = false,
                Status = BackupStatus.Failed,
                Message = $"Restore failed: {ex.Message}",
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTime.Now
            };
        }
        finally
        {
            // Temporary plaintext copies are removed whether the restore succeeded or not.
            if (tempExtractPath != null && Directory.Exists(tempExtractPath))
            {
                try { Directory.Delete(tempExtractPath, true); } catch (IOException ex) { _logger.LogDebug(ex, "RestoreBackupAsync failed"); } catch (UnauthorizedAccessException ex) { _logger.LogDebug(ex, "RestoreBackupAsync failed"); }
            }
            if (decryptedArchivePath != null)
            {
                try { File.Delete(decryptedArchivePath); } catch (IOException ex) { _logger.LogDebug(ex, "RestoreBackupAsync failed"); } catch (UnauthorizedAccessException ex) { _logger.LogDebug(ex, "RestoreBackupAsync failed"); }
            }
        }
    }

    // ==================== BACKUP MANAGEMENT ====================

    public Task<List<BackupArchive>> GetBackupHistoryAsync(string? backupLocation = null) => LoadArchivesAsync(onlyPresent: true);

    /// <summary>Every recorded backup, newest first; with <paramref name="onlyPresent"/>, those whose files are still there.</summary>
    private async Task<List<BackupArchive>> LoadArchivesAsync(bool onlyPresent)
    {
        return await Task.Run(() =>
        {
            var archives = new List<BackupArchive>();

            try
            {
                var metadataFiles = Directory.GetFiles(_backupMetadataFolder, "*.json");
                foreach (var file in metadataFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var archive = JsonSerializer.Deserialize<BackupArchive>(json);
                        if (archive != null)
                        {
                            // Check if backup file still exists
                            if (!onlyPresent || File.Exists(archive.FilePath) || Directory.Exists(archive.FilePath))
                            {
                                archives.Add(archive);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "GetBackupHistoryAsync failed");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetBackupHistoryAsync failed");
            }

            return archives.OrderByDescending(a => a.CreatedDate).ToList();
        });
    }

    /// <summary>
    /// Verifies the backup itself: every manifest entry that has a recorded SHA-256 is read from the backup
    /// (decrypting it first when encrypted) and compared with that hash. The original source files are not read.
    /// </summary>
    public async Task<BackupResult> VerifyBackupAsync(BackupArchive archive, IProgress<BackupProgress>? progress = null,
        string? password = null, CancellationToken cancellationToken = default)
    {
        string? decryptedArchivePath = null;
        try
        {
            if (archive.Type == BackupType.SystemImage)
            {
                return Failed("Windows Backup checks a system image as it writes it, and its folder is closed to " +
                              "this app, so there is nothing more it can check.");
            }

            if (archive.Manifest == null)
            {
                return Failed("No manifest available for verification");
            }

            // An incremental taken after files were only deleted copies nothing: there is nothing in it to be wrong.
            if (archive.Manifest.Files.Count == 0 && archive.Manifest.ParentBackupId != null)
            {
                return new BackupResult
                {
                    Success = true,
                    Status = BackupStatus.Completed,
                    Message = "Backup verified: it holds no files of its own, only which files were deleted since the backup before it"
                };
            }

            var hashedFiles = archive.Manifest.Files.Where(f => !string.IsNullOrEmpty(f.Hash)).ToList();
            if (hashedFiles.Count == 0)
            {
                return Failed("This backup was created without checksums, so it cannot be verified");
            }

            var sourcePath = archive.FilePath;
            if (archive.IsEncrypted || sourcePath.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(password))
                {
                    return Failed("This backup is encrypted. Enter its password to verify it.");
                }

                decryptedArchivePath = Path.Combine(Path.GetTempPath(), $"verify_{Guid.NewGuid():N}.zip");
                try
                {
                    await BackupEncryption.DecryptFileAsync(sourcePath, decryptedArchivePath, password, cancellationToken);
                }
                catch (BackupPasswordException ex)
                {
                    return Failed(ex.Message);
                }
                sourcePath = decryptedArchivePath;
            }

            progress?.Report(new BackupProgress
            {
                Status = BackupStatus.Running,
                CurrentOperation = "Verifying backup...",
                TotalFiles = hashedFiles.Count
            });

            var verified = 0;
            var problems = new List<BackupError>();

            if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                using var zip = ZipFile.OpenRead(sourcePath);
                var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    entries.TryAdd(NormalizeEntryName(entry.FullName), entry);
                }

                foreach (var file in hashedFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!entries.TryGetValue(NormalizeEntryName(file.RelativePath), out var entry))
                    {
                        problems.Add(new BackupError { FilePath = file.RelativePath, ErrorMessage = "Missing from backup" });
                        continue;
                    }

                    await using var stream = entry.Open();
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                    if (string.Equals(hash, file.Hash, StringComparison.OrdinalIgnoreCase))
                        verified++;
                    else
                        problems.Add(new BackupError { FilePath = file.RelativePath, ErrorMessage = "Checksum mismatch" });
                }
            }
            else if (Directory.Exists(sourcePath))
            {
                foreach (var file in hashedFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var path = Path.Combine(sourcePath, file.RelativePath);
                    if (!File.Exists(path))
                    {
                        problems.Add(new BackupError { FilePath = file.RelativePath, ErrorMessage = "Missing from backup" });
                        continue;
                    }

                    await using var stream = File.OpenRead(path);
                    var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
                    if (string.Equals(hash, file.Hash, StringComparison.OrdinalIgnoreCase))
                        verified++;
                    else
                        problems.Add(new BackupError { FilePath = file.RelativePath, ErrorMessage = "Checksum mismatch" });
                }
            }
            else
            {
                return Failed("Backup file not found");
            }

            var ok = problems.Count == 0;

            // Files the backup recorded without a checksum were filtered out of hashedFiles above, so
            // nothing was ever compared for them. "All N files match" counted only the ones that could be
            // checked, and said nothing about the rest - which is the part a reader would want to know.
            var unchecked_ = archive.Manifest.Files.Count - hashedFiles.Count;
            var uncheckedNote = unchecked_ > 0
                ? $"; {unchecked_} file(s) were stored without a checksum and could not be checked"
                : "";

            return new BackupResult
            {
                Success = ok,
                Status = ok ? BackupStatus.Completed : BackupStatus.Failed,
                Message = ok
                    ? $"Backup verified: {verified} file(s) match their recorded checksums{uncheckedNote}"
                    : $"Backup verification failed: {problems.Count} file(s) missing or changed in the backup ({verified} verified){uncheckedNote}",
                ProcessedFiles = verified,
                FailedFiles = problems.Count,
                Errors = problems
            };
        }
        catch (OperationCanceledException)
        {
            return new BackupResult { Success = false, Status = BackupStatus.Cancelled, Message = "Verification was cancelled" };
        }
        catch (Exception ex)
        {
            return Failed($"Verification failed: {ex.Message}");
        }
        finally
        {
            if (decryptedArchivePath != null)
            {
                try { File.Delete(decryptedArchivePath); } catch (IOException ex) { _logger.LogDebug(ex, "VerifyBackupAsync failed"); } catch (UnauthorizedAccessException ex) { _logger.LogDebug(ex, "VerifyBackupAsync failed"); }
            }
        }

        static BackupResult Failed(string message) => new() { Success = false, Status = BackupStatus.Failed, Message = message };
    }

    private static string NormalizeEntryName(string name) => name.Replace('\\', '/').TrimStart('/');

    /// <summary>
    /// Deletes a backup, unless a later incremental builds on it: that one could not be restored without it.
    /// </summary>
    public async Task<BackupResult> DeleteBackupAsync(BackupArchive archive)
    {
        var dependents = (await GetBackupHistoryAsync()).Count(a => a.ParentBackupId == archive.Id);
        if (dependents > 0)
        {
            return new BackupResult
            {
                Success = false,
                Status = BackupStatus.Failed,
                Message = $"Not deleted: {dependents} later incremental backup(s) build on this one and could not be " +
                          "restored without it. Delete the newer incrementals first."
            };
        }

        return await DeleteArchiveAsync(archive);
    }

    private async Task<BackupResult> DeleteArchiveAsync(BackupArchive archive)
    {
        return await Task.Run(() =>
        {
            try
            {
                // WindowsImageBackup holds every version Windows keeps, and other computers' images too; the app
                // takes it off the list and leaves the files to Windows, which manages them.
                if (archive.Type == BackupType.SystemImage)
                {
                    TryDeleteFile(Path.Combine(_backupMetadataFolder, $"{archive.Id}.json"));
                    return new BackupResult
                    {
                        Success = true,
                        Status = BackupStatus.Completed,
                        Message = $"Removed from the list. The image is still in {archive.FilePath}; to free its space, use " +
                                  "Control Panel > Backup and Restore (Windows 7) > Manage space."
                    };
                }

                // Delete backup file/folder
                if (File.Exists(archive.FilePath))
                {
                    File.Delete(archive.FilePath);
                }
                else if (Directory.Exists(archive.FilePath))
                {
                    Directory.Delete(archive.FilePath, true);
                }

                // Delete metadata
                var metadataPath = Path.Combine(_backupMetadataFolder, $"{archive.Id}.json");
                if (File.Exists(metadataPath))
                {
                    File.Delete(metadataPath);
                }

                return new BackupResult
                {
                    Success = true,
                    Status = BackupStatus.Completed,
                    Message = "Backup deleted successfully"
                };
            }
            catch (Exception ex)
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = $"Delete failed: {ex.Message}"
                };
            }
        });
    }

    // ==================== SCHEDULING ====================

    public async Task<bool> ScheduleBackupAsync(BackupSchedule schedule)
    {
        return await Task.Run(() =>
        {
            try
            {
                var schedulePath = Path.Combine(_backupMetadataFolder, "schedules");
                Directory.CreateDirectory(schedulePath);

                var filePath = Path.Combine(schedulePath, $"{schedule.Id}.json");
                var json = JsonSerializer.Serialize(schedule, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(filePath, json);

                // Calculate next run time
                schedule.NextRunTime = CalculateNextRunTime(schedule);

                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> RemoveScheduledBackupAsync(string scheduleId)
    {
        return await Task.Run(() =>
        {
            try
            {
                var filePath = Path.Combine(_backupMetadataFolder, "schedules", $"{scheduleId}.json");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<List<BackupSchedule>> GetScheduledBackupsAsync()
    {
        return await Task.Run(() =>
        {
            var schedules = new List<BackupSchedule>();
            var schedulePath = Path.Combine(_backupMetadataFolder, "schedules");

            if (!Directory.Exists(schedulePath)) return schedules;

            foreach (var file in Directory.GetFiles(schedulePath, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var schedule = JsonSerializer.Deserialize<BackupSchedule>(json);
                    if (schedule != null)
                    {
                        schedules.Add(schedule);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "GetScheduledBackupsAsync failed");
                }
            }

            return schedules;
        });
    }

    private static DateTime? CalculateNextRunTime(BackupSchedule schedule)
    {
        var now = DateTime.Now;
        var today = now.Date.Add(schedule.TimeOfDay);

        return schedule.Frequency switch
        {
            BackupFrequency.Daily => today <= now ? today.AddDays(1) : today,
            BackupFrequency.Weekly => GetNextWeekday(now, schedule.DayOfWeek ?? DayOfWeek.Sunday, schedule.TimeOfDay),
            BackupFrequency.Monthly => GetNextMonthDay(now, schedule.DayOfMonth ?? 1, schedule.TimeOfDay),
            BackupFrequency.Once => today,
            _ => null
        };
    }

    private static DateTime GetNextWeekday(DateTime from, DayOfWeek dayOfWeek, TimeSpan timeOfDay)
    {
        var daysUntil = ((int)dayOfWeek - (int)from.DayOfWeek + 7) % 7;
        if (daysUntil == 0 && from.TimeOfDay > timeOfDay) daysUntil = 7;
        return from.Date.AddDays(daysUntil).Add(timeOfDay);
    }

    private static DateTime GetNextMonthDay(DateTime from, int dayOfMonth, TimeSpan timeOfDay)
    {
        var target = new DateTime(from.Year, from.Month, Math.Min(dayOfMonth, DateTime.DaysInMonth(from.Year, from.Month))).Add(timeOfDay);
        if (target <= from) target = target.AddMonths(1);
        return target;
    }

    // ==================== UTILITIES ====================

    public async Task<DriveSpaceInfo> GetDriveSpaceAsync(string drivePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(drivePath) ?? drivePath);
                return new DriveSpaceInfo
                {
                    DrivePath = drive.Name,
                    DriveLabel = drive.IsReady ? drive.VolumeLabel : "",
                    DriveType = drive.DriveType.ToString(),
                    TotalBytes = drive.IsReady ? drive.TotalSize : 0,
                    FreeBytes = drive.IsReady ? drive.AvailableFreeSpace : 0,
                    IsReady = drive.IsReady,
                    IsRemovable = drive.DriveType == DriveType.Removable
                };
            }
            catch
            {
                return new DriveSpaceInfo { DrivePath = drivePath };
            }
        });
    }

    public async Task<List<DriveInfo>> GetAvailableDrivesAsync()
    {
        return await Task.Run(() =>
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady && d.DriveType != DriveType.CDRom)
                .ToList();
        });
    }

    public async Task<long> EstimateBackupSizeAsync(BackupJob job)
    {
        return await Task.Run(async () =>
        {
            var files = await CollectFilesAsync(job, CancellationToken.None);
            return files.Sum(f => f.Length);
        });
    }

    // ==================== HELPER METHODS ====================

    /// <summary>
    /// The folder each source is copied into inside a backup. Its own name where that is unique; a drive root,
    /// which has no name of its own, is <c>Drive-D</c>; and two sources sharing a name - D:\A\Photos and
    /// E:\Photos - are told apart by a number, rather than one being written over the other.
    /// </summary>
    internal static Dictionary<string, string> SourceFolderNames(IEnumerable<string> sources)
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var source in sources)
        {
            if (names.ContainsKey(source))
                continue;

            var trimmed = source.TrimEnd('\\', '/');
            var name = trimmed[(trimmed.LastIndexOfAny(['\\', '/']) + 1)..];
            if (string.IsNullOrEmpty(name) || trimmed.EndsWith(':'))
                name = "Drive-" + new string(trimmed.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrEmpty(name) || name == "Drive-")
                name = "Files";

            var unique = name;
            for (var n = 2; !taken.Add(unique); n++)
                unique = $"{name}-{n}";

            names[source] = unique;
        }

        return names;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: this runs while a failure is already being reported, and that failure is the one to tell.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: as above - the encryption error is what the user needs to hear.
        }
    }

    private async Task<List<FileInfo>> CollectFilesAsync(BackupJob job, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var files = new List<FileInfo>();

            foreach (var sourcePath in job.SourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(sourcePath))
                {
                    files.Add(new FileInfo(sourcePath));
                }
                else if (Directory.Exists(sourcePath))
                {
                    // Skips what this account cannot read - Documents' legacy My Music junction denies listing, and
                    // one denied folder used to fail the whole backup - and never follows a link out of the source.
                    var dirFiles = FileScanning.EnumerateEveryFile(sourcePath, cancellationToken)
                        .Select(path => new FileInfo(path))
                        .Where(f => ShouldIncludeFile(f, job));

                    files.AddRange(dirFiles);
                }
            }

            return files;
        }, cancellationToken);
    }

    private static bool ShouldIncludeFile(FileInfo file, BackupJob job)
    {
        // Check exclusion paths, on whole folders: excluding ...\Temp leaves ...\TempProjects in
        if (job.ExcludePaths.Any(p => string.Equals(file.FullName, p, StringComparison.OrdinalIgnoreCase)
                                      || PathHelper.IsPathWithinDirectory(file.FullName, p)))
            return false;

        // Check exclusion patterns
        if (job.ExcludePatterns.Any(p => MatchesPattern(file.Name, p)))
            return false;

        // Check hidden files
        if (!job.IncludeHiddenFiles && file.Attributes.HasFlag(FileAttributes.Hidden))
            return false;

        // Check system files
        if (!job.IncludeSystemFiles && file.Attributes.HasFlag(FileAttributes.System))
            return false;

        return true;
    }

    private static bool MatchesPattern(string fileName, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(fileName, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// The files a backup stands for. Backups written before the catalog existed stand for the files they hold.
    /// </summary>
    internal static List<BackupCatalogEntry> CatalogOf(BackupManifest manifest) =>
        manifest.Catalog ?? manifest.Files.Select(f => new BackupCatalogEntry
        {
            OriginalPath = f.OriginalPath,
            SizeBytes = f.SizeBytes,
            ModifiedDate = f.ModifiedDate.ToUniversalTime()
        }).ToList();

    private static Dictionary<string, BackupCatalogEntry> CatalogByPath(IEnumerable<BackupCatalogEntry> catalog)
    {
        var byPath = new Dictionary<string, BackupCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in catalog)
            byPath.TryAdd(entry.OriginalPath, entry);
        return byPath;
    }

    /// <summary>
    /// New, or not as the last backup saw it. Any difference counts, not only a newer date: a file put back from
    /// an older copy has an older date and is still a change.
    /// </summary>
    private static bool IsNewOrChanged(FileInfo file, IReadOnlyDictionary<string, BackupCatalogEntry> catalog) =>
        !catalog.TryGetValue(file.FullName, out var seen)
        || seen.SizeBytes != file.Length
        || seen.ModifiedDate.ToUniversalTime() != file.LastWriteTimeUtc;

    /// <summary>
    /// What this backup stands for: every source file as it is now, except one this run failed to copy, which
    /// keeps the earlier backup's entry - that copy is still the one to restore, and the next incremental, seeing
    /// the file differ from it, tries again.
    /// </summary>
    private static List<BackupCatalogEntry> BuildCatalog(
        IEnumerable<FileInfo> sourceFiles, IReadOnlySet<string> notCopied, IReadOnlyDictionary<string, BackupCatalogEntry>? parentCatalog)
    {
        var catalog = new List<BackupCatalogEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in sourceFiles)
        {
            if (!seen.Add(file.FullName))
                continue;

            if (notCopied.Contains(file.FullName))
            {
                if (parentCatalog != null && parentCatalog.TryGetValue(file.FullName, out var earlier))
                    catalog.Add(earlier);
                continue;
            }

            catalog.Add(new BackupCatalogEntry
            {
                OriginalPath = file.FullName,
                SizeBytes = file.Length,
                ModifiedDate = file.LastWriteTimeUtc
            });
        }

        return catalog;
    }

    /// <summary>
    /// The backup an incremental builds on: the newest full or incremental of the same name whose chain is whole.
    /// When there is none it can use, the reason, so the result can say why this one holds every file.
    /// </summary>
    private async Task<(BackupArchive? Parent, string Reason)> FindParentAsync(BackupJob job, CancellationToken cancellationToken)
    {
        var history = await GetBackupHistoryAsync();
        var byId = ById(history);

        var parent = history
            .Where(a => a.Name == job.Name && a.Manifest != null)
            .Where(a => a.Type == BackupType.Full || (a.Type == BackupType.Incremental && a.Manifest!.Catalog != null))
            .Where(a => ChainOf(a, byId) != null)
            .OrderByDescending(a => a.CreatedDate)
            .FirstOrDefault();

        if (parent == null)
            return (null, $"there was no earlier backup named \"{job.Name}\" to build on");

        // One chain, one password: restoring an incremental opens every backup it builds on with the same one.
        if (parent.IsEncrypted != job.EnableEncryption)
            return (null, parent.IsEncrypted
                ? $"the backup of {parent.CreatedDate:g} is encrypted and this one is not"
                : $"the backup of {parent.CreatedDate:g} is not encrypted and this one is");

        if (parent.IsEncrypted && !await BackupEncryption.PasswordOpensAsync(parent.FilePath, job.EncryptionPassword!, cancellationToken))
            return (null, $"the backup of {parent.CreatedDate:g} was encrypted with a different password");

        return (parent, "");
    }

    private static Dictionary<string, BackupArchive> ById(IEnumerable<BackupArchive> archives)
    {
        var byId = new Dictionary<string, BackupArchive>(StringComparer.Ordinal);
        foreach (var archive in archives)
            byId.TryAdd(archive.Id, archive);
        return byId;
    }

    /// <summary>
    /// The backup and every one it builds on, oldest first; null when one of them is gone, since the files it
    /// held cannot then be put back.
    /// </summary>
    internal static List<BackupArchive>? ChainOf(BackupArchive archive, IReadOnlyDictionary<string, BackupArchive> byId)
    {
        var chain = new List<BackupArchive> { archive };
        var seen = new HashSet<string>(StringComparer.Ordinal) { archive.Id };

        for (var current = archive; current.ParentBackupId != null;)
        {
            if (!byId.TryGetValue(current.ParentBackupId, out var parent) || !seen.Add(parent.Id))
                return null;
            chain.Add(parent);
            current = parent;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>
    /// Which backup in a chain each file is restored from: every file the newest one stands for, taken from the
    /// newest backup that holds a copy of it. A file deleted before the newest backup is in none, so it stays deleted.
    /// </summary>
    internal static List<(BackupArchive Archive, List<BackupFileEntry> Entries)> AssignLayers(
        IReadOnlyList<(BackupArchive Archive, BackupManifest Manifest)> chain, List<string>? selectiveFiles, out int unheld)
    {
        var wanted = new HashSet<string>(CatalogOf(chain[^1].Manifest).Select(e => e.OriginalPath), StringComparer.OrdinalIgnoreCase);
        var selective = selectiveFiles?.ToHashSet(StringComparer.Ordinal);
        var layers = new List<(BackupArchive, List<BackupFileEntry>)>();

        for (var i = chain.Count - 1; i >= 0; i--)
        {
            var entries = chain[i].Manifest.Files
                .Where(f => wanted.Remove(f.OriginalPath))
                .Where(f => selective == null || selective.Contains(f.RelativePath))
                .ToList();
            layers.Add((chain[i].Archive, entries));
        }

        unheld = selective == null ? wanted.Count : 0;
        layers.Reverse();
        return layers;
    }

    private async Task<List<BackupArchive>?> ChainForAsync(BackupArchive archive)
    {
        var byId = ById(await GetBackupHistoryAsync());
        byId.TryAdd(archive.Id, archive);
        return ChainOf(archive, byId);
    }

    private static string MissingChainMessage(BackupArchive archive) =>
        $"\"{archive.Name}\" is an incremental backup, and a backup it builds on has been deleted or moved, " +
        "so it cannot be restored. Restore the newest full backup instead.";

    private static async Task CopyFileWithProgressAsync(string source, string destination, CancellationToken cancellationToken)
    {
        const int bufferSize = 81920; // 80KB buffer
        await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

        await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);
    }

    private static async Task<string> CalculateFileHashAsync(string filePath)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);
            var hash = await SHA256.HashDataAsync(stream);
            return Convert.ToHexString(hash);
        }
        catch
        {
            return "";
        }
    }

    private async Task CompressBackupAsync(string sourcePath, string zipPath, BackupCompression compression, CancellationToken cancellationToken)
    {
        var level = compression switch
        {
            BackupCompression.Fast => CompressionLevel.Fastest,
            BackupCompression.Normal => CompressionLevel.Optimal,
            BackupCompression.Maximum => CompressionLevel.SmallestSize,
            _ => CompressionLevel.NoCompression
        };

        await Task.Run(() => ZipFile.CreateFromDirectory(sourcePath, zipPath, level, false), cancellationToken);
    }

    /// <summary>Size on disk of a backup: the archive file, or every file in an uncompressed backup folder.</summary>
    private static long GetBackupSize(string backupPath) =>
        Directory.Exists(backupPath)
            ? new DirectoryInfo(backupPath).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : new FileInfo(backupPath).Length;

    private async Task SaveManifestAsync(BackupManifest manifest, string path)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    /// <summary>
    /// Works out what a restore would write, before it writes anything. Every path in the archive is checked:
    /// a backup can be edited by anyone who can reach the file, and an edited one would otherwise choose its
    /// own destinations - the Startup folder, say - and have the restore write them as the user.
    /// </summary>
    internal static RestorePlan PlanRestore(
        BackupManifest manifest,
        IEnumerable<BackupFileEntry> entries,
        string extractPath,
        string destinationPath,
        RestoreOptions options)
    {
        var files = new List<PlannedRestore>();
        var refused = new List<BackupError>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sourceRoots = manifest.SourceRoots
            .Where(rootPath => !string.IsNullOrWhiteSpace(rootPath))
            .Select(FullPathOrNull)
            .Where(rootPath => rootPath != null)
            .Select(rootPath => rootPath!)
            .ToList();

        var extractRoot = FullPathOrNull(extractPath);
        var destinationRoot = FullPathOrNull(destinationPath);

        foreach (var entry in entries)
        {
            // Where it comes from: inside the extracted backup, never up and out of it.
            var sourceFile = CombineWithin(extractRoot, entry.RelativePath);
            if (sourceFile == null)
            {
                refused.Add(Refuse(entry, "its place in the backup points outside the backup"));
                continue;
            }

            string? destination;
            if (options.RestoreToOriginalLocation)
            {
                destination = FullPathOrNull(entry.OriginalPath);
                if (destination == null || !Path.IsPathFullyQualified(destination))
                {
                    refused.Add(Refuse(entry, "it does not say where it came from"));
                    continue;
                }

                // Back inside the folders the backup was taken from, and nowhere else.
                if (sourceRoots.Count > 0 && !sourceRoots.Any(rootPath => IsInside(rootPath, destination)))
                {
                    refused.Add(Refuse(entry, $"it asks to be written to {destination}, outside the folders this backup was taken from"));
                    continue;
                }
            }
            else
            {
                destination = CombineWithin(destinationRoot, entry.RelativePath);
                if (destination == null)
                {
                    refused.Add(Refuse(entry, "its place in the backup points outside the folder being restored to"));
                    continue;
                }
            }

            files.Add(new PlannedRestore(entry, sourceFile, destination));

            var folder = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(folder))
            {
                folders.Add(folder);
            }
        }

        return new RestorePlan
        {
            Files = files,
            DestinationFolders = folders.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList(),
            Refused = refused,
            HasRecordedSourceRoots = sourceRoots.Count > 0,
        };
    }

    /// <summary>
    /// What a restore of this archive would write. Callers show the folders to the person before agreeing to
    /// it; RestoreBackupAsync applies the same rules whether they do or not.
    /// </summary>
    public async Task<RestorePlan> PrepareRestoreAsync(
        BackupArchive archive, string destinationPath, RestoreOptions options, CancellationToken cancellationToken = default)
    {
        // Nothing is extracted for a preview, so the place files would be read from stands in for the real
        // one; it is only used to check that no entry points out of the backup.
        var notionalExtractPath = Path.Combine(Path.GetTempPath(), "sysmonitor-restore-preview");

        if (archive.Type == BackupType.SystemImage)
            return new RestorePlan { Problem = SystemImage.RestoreInstructions };

        if (archive.ParentBackupId != null)
        {
            var chain = await ChainForAsync(archive);
            if (chain == null)
                return new RestorePlan { Problem = MissingChainMessage(archive) };

            var read = new List<(BackupArchive, BackupManifest)>();
            foreach (var member in chain)
            {
                var memberManifest = await ReadManifestOnlyAsync(member, options, cancellationToken);
                if (memberManifest == null)
                {
                    return new RestorePlan
                    {
                        Problem = $"The backup of {member.CreatedDate:g}, which this incremental builds on, could not be read" +
                                  (member.IsEncrypted ? " with this password." : ".")
                    };
                }
                read.Add((member, memberManifest));
            }

            var plans = AssignLayers(read, options.SelectiveFiles, out _)
                .Select((layer, i) => PlanRestore(read[i].Item2, layer.Entries, notionalExtractPath, destinationPath, options))
                .ToList();

            return new RestorePlan
            {
                Files = plans.SelectMany(p => p.Files).ToList(),
                DestinationFolders = plans.SelectMany(p => p.DestinationFolders)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                Refused = plans.SelectMany(p => p.Refused).ToList(),
                HasRecordedSourceRoots = plans.All(p => p.HasRecordedSourceRoots),
            };
        }

        var manifest = await ReadManifestOnlyAsync(archive, options, cancellationToken);
        if (manifest == null)
        {
            return new RestorePlan();
        }

        var entries = options.SelectiveFiles != null
            ? manifest.Files.Where(f => options.SelectiveFiles.Contains(f.RelativePath)).ToList()
            : manifest.Files;

        return PlanRestore(manifest, entries, notionalExtractPath, destinationPath, options);
    }

    /// <summary>Reads only the manifest out of a backup, decrypting it in a temporary file when it has to.</summary>
    private async Task<BackupManifest?> ReadManifestOnlyAsync(BackupArchive archive, RestoreOptions options, CancellationToken cancellationToken)
    {
        string? decrypted = null;

        try
        {
            var path = archive.FilePath;

            if (archive.IsEncrypted || path.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(options.Password))
                {
                    return null;
                }

                decrypted = Path.Combine(Path.GetTempPath(), $"restore_preview_{Guid.NewGuid():N}.zip");
                await BackupEncryption.DecryptFileAsync(path, decrypted, options.Password, cancellationToken);
                path = decrypted;
            }

            if (Directory.Exists(path))
            {
                var manifestPath = Path.Combine(path, _manifestFileName);
                return File.Exists(manifestPath) ? await LoadManifestAsync(manifestPath) : null;
            }

            using var zip = System.IO.Compression.ZipFile.OpenRead(path);
            var entry = zip.GetEntry(_manifestFileName);
            if (entry == null)
            {
                return null;
            }

            using var stream = entry.Open();
            return await JsonSerializer.DeserializeAsync<BackupManifest>(stream, cancellationToken: cancellationToken);
        }
        catch (Exception)
        {
            return null;
        }
        finally
        {
            if (decrypted != null)
            {
                try { File.Delete(decrypted); } catch (Exception ex) { _logger.LogDebug(ex, "ReadManifestOnlyAsync failed"); }
            }
        }
    }

    private static BackupError Refuse(BackupFileEntry entry, string reason) => new()
    {
        FilePath = string.IsNullOrEmpty(entry.OriginalPath) ? entry.RelativePath : entry.OriginalPath,
        ErrorMessage = $"Refused: {reason}.",
    };

    /// <summary>A full path, or null when the text is not one.</summary>
    private static string? FullPathOrNull(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>A path under a root, or null when the relative part climbs out of it.</summary>
    private static string? CombineWithin(string? root, string relativePath)
    {
        if (root == null || string.IsNullOrWhiteSpace(relativePath)) return null;
        if (Path.IsPathRooted(relativePath)) return null;

        var combined = FullPathOrNull(Path.Combine(root, relativePath));
        return combined != null && IsInside(root, combined) ? combined : null;
    }

    /// <summary>A restore may write to the destination folder itself as well as anywhere under it.</summary>
    private static bool IsInside(string root, string candidate) =>
        PathHelper.IsPathWithinDirectory(candidate, root) || PathHelper.IsSamePath(candidate, root);

    private async Task<BackupManifest> LoadManifestAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<BackupManifest>(json) ?? new BackupManifest();
    }

    private async Task SaveArchiveMetadataAsync(BackupArchive archive)
    {
        var path = Path.Combine(_backupMetadataFolder, $"{archive.Id}.json");
        var json = JsonSerializer.Serialize(archive, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

    private async Task CleanupOldBackupsAsync(BackupJob job)
    {
        var archives = await GetBackupHistoryAsync();
        var byId = ById(archives);
        var jobArchives = archives
            .Where(a => a.Name == job.Name)
            .OrderByDescending(a => a.CreatedDate)
            .ToList();

        var cutoffDate = DateTime.Now.AddDays(-job.MaxAgeDays);
        var expired = jobArchives.Skip(job.MaxBackupsToKeep)
            .Concat(jobArchives.Where(a => a.CreatedDate < cutoffDate))
            .Select(a => a.Id)
            .ToHashSet(StringComparer.Ordinal);

        // Neither the count nor the age removes a backup that one being kept builds on, since that one could not
        // be restored without it; nor the newest full backup, which incrementals written before chains were
        // recorded were taken against.
        var needed = jobArchives
            .Where(a => !expired.Contains(a.Id))
            .SelectMany(a => ChainOf(a, byId) ?? [a])
            .Select(a => a.Id)
            .ToHashSet(StringComparer.Ordinal);
        var newestFull = jobArchives.FirstOrDefault(a => a.Type == BackupType.Full);
        if (newestFull != null)
            needed.Add(newestFull.Id);

        foreach (var archive in jobArchives.Where(a => expired.Contains(a.Id) && !needed.Contains(a.Id)))
        {
            await DeleteArchiveAsync(archive);
        }
    }
}
