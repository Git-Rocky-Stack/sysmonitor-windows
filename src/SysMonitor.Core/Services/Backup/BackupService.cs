using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SysMonitor.Core.Services.Backup;

/// <summary>
/// Comprehensive backup service implementation for Windows
/// </summary>
public class BackupService : IBackupService
{
    private readonly string _backupMetadataFolder;
    private readonly string _manifestFileName = "backup_manifest.json";
    private bool _isBackupInProgress;
    private CancellationTokenSource? _currentBackupCts;

    public bool IsBackupInProgress => _isBackupInProgress;

    public BackupService()
    {
        _backupMetadataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "Backups");
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

            // Create backup destination folder
            var backupFolderName = $"Backup_{job.Name}_{DateTime.Now:yyyyMMdd_HHmmss}";
            var backupPath = Path.Combine(job.DestinationPath, backupFolderName);
            Directory.CreateDirectory(backupPath);

            // Collect files to backup
            progress?.Report(new BackupProgress
            {
                Status = BackupStatus.Running,
                CurrentOperation = "Scanning files...",
                CurrentFile = ""
            });

            var filesToBackup = await CollectFilesAsync(job, _currentBackupCts.Token);
            var totalFiles = filesToBackup.Count;
            var totalBytes = filesToBackup.Sum(f => f.Length);

            if (totalFiles == 0)
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = "No files found to backup"
                };
            }

            // For incremental backup, filter to only changed files
            if (job.Type == BackupType.Incremental)
            {
                var lastBackup = await GetLastBackupManifestAsync(job);
                if (lastBackup != null)
                {
                    filesToBackup = FilterChangedFiles(filesToBackup, lastBackup);
                    totalFiles = filesToBackup.Count;
                    totalBytes = filesToBackup.Sum(f => f.Length);
                }
            }

            // Create manifest
            var manifest = new BackupManifest
            {
                BackupId = Guid.NewGuid().ToString(),
                CreatedDate = DateTime.Now,
                Files = [],
                Metadata = new Dictionary<string, string>
                {
                    ["BackupType"] = job.Type.ToString(),
                    ["ComputerName"] = Environment.MachineName,
                    ["UserName"] = Environment.UserName,
                    ["BackupName"] = job.Name
                }
            };

            // Backup files
            var lastProgressUpdate = DateTime.Now;
            var bytesSinceLastUpdate = 0L;

            foreach (var file in filesToBackup)
            {
                _currentBackupCts.Token.ThrowIfCancellationRequested();

                try
                {
                    // Calculate relative path
                    var sourcePath = job.SourcePaths.FirstOrDefault(s => file.FullName.StartsWith(s, StringComparison.OrdinalIgnoreCase));
                    var relativePath = sourcePath != null
                        ? Path.GetRelativePath(sourcePath, file.FullName)
                        : file.Name;

                    // Preserve folder structure
                    var sourceRootName = sourcePath != null ? new DirectoryInfo(sourcePath).Name : "Files";
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
                catch (Exception ex)
                {
                    failedFiles++;
                    errors.Add(new BackupError
                    {
                        FilePath = file.FullName,
                        ErrorMessage = ex.Message
                    });
                }
            }

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

                var encryptedPath = finalBackupPath + ".enc";
                await EncryptFileAsync(finalBackupPath, encryptedPath, job.EncryptionPassword, _currentBackupCts.Token);
                File.Delete(finalBackupPath);
                finalBackupPath = encryptedPath;
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
                SizeBytes = new FileInfo(finalBackupPath).Length,
                FileCount = processedFiles,
                IsEncrypted = job.EnableEncryption,
                IsVerified = job.VerifyAfterBackup,
                Description = job.Description,
                SourcePaths = job.SourcePaths,
                Manifest = manifest
            };

            // Save archive metadata
            await SaveArchiveMetadataAsync(archive);

            // Cleanup old backups
            await CleanupOldBackupsAsync(job);

            var status = failedFiles > 0 ? BackupStatus.PartialSuccess : BackupStatus.Completed;

            return new BackupResult
            {
                Success = true,
                Status = status,
                Message = failedFiles > 0
                    ? $"Backup completed with {failedFiles} errors"
                    : "Backup completed successfully",
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

    // ==================== SYSTEM IMAGE BACKUP ====================

    public async Task<BackupResult> CreateSystemImageAsync(string destinationPath, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            progress?.Report(new BackupProgress
            {
                Status = BackupStatus.Running,
                CurrentOperation = "Creating system image backup...",
                CurrentFile = "Using Windows Backup (wbadmin)"
            });

            // Use wbadmin for system image
            var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            var destinationDrive = Path.GetPathRoot(destinationPath);

            if (string.IsNullOrEmpty(destinationDrive))
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = "Invalid destination path"
                };
            }

            // wbadmin requires admin privileges
            var psi = new ProcessStartInfo
            {
                FileName = "wbadmin",
                Arguments = $"start backup -backupTarget:{destinationDrive} -include:{systemDrive} -allCritical -quiet",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = "Failed to start wbadmin"
                };
            }

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            stopwatch.Stop();

            if (process.ExitCode == 0)
            {
                return new BackupResult
                {
                    Success = true,
                    Status = BackupStatus.Completed,
                    Message = "System image created successfully",
                    OutputPath = destinationPath,
                    Duration = stopwatch.Elapsed,
                    StartTime = startTime,
                    EndTime = DateTime.Now
                };
            }
            else
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = $"System image failed: {error}",
                    Duration = stopwatch.Elapsed,
                    StartTime = startTime,
                    EndTime = DateTime.Now
                };
            }
        }
        catch (Exception ex)
        {
            return new BackupResult
            {
                Success = false,
                Status = BackupStatus.Failed,
                Message = $"System image error: {ex.Message}",
                Duration = stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTime.Now
            };
        }
    }

    // ==================== RESTORE POINT ====================

    public async Task<BackupResult> CreateRestorePointAsync(string description)
    {
        return await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-Command \"Checkpoint-Computer -Description '{description}' -RestorePointType 'MODIFY_SETTINGS'\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    Verb = "runas"
                };

                using var process = Process.Start(psi);
                process?.WaitForExit(60000); // 1 minute timeout

                if (process?.ExitCode == 0)
                {
                    return new BackupResult
                    {
                        Success = true,
                        Status = BackupStatus.Completed,
                        Message = $"Restore point created: {description}"
                    };
                }
                else
                {
                    return new BackupResult
                    {
                        Success = false,
                        Status = BackupStatus.Failed,
                        Message = "Failed to create restore point (requires admin privileges)"
                    };
                }
            }
            catch (Exception ex)
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = $"Restore point error: {ex.Message}"
                };
            }
        });
    }

    public async Task<List<RestorePointInfo>> GetRestorePointsAsync()
    {
        return await Task.Run(() =>
        {
            var restorePoints = new List<RestorePointInfo>();

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = "-Command \"Get-ComputerRestorePoint | ConvertTo-Json\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return restorePoints;

                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    // Parse JSON output
                    using var doc = JsonDocument.Parse(output);
                    var root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in root.EnumerateArray())
                        {
                            restorePoints.Add(ParseRestorePoint(item));
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        restorePoints.Add(ParseRestorePoint(root));
                    }
                }
            }
            catch { }

            return restorePoints;
        });
    }

    private static RestorePointInfo ParseRestorePoint(JsonElement element)
    {
        return new RestorePointInfo
        {
            SequenceNumber = element.TryGetProperty("SequenceNumber", out var seq) ? seq.GetInt32() : 0,
            Description = element.TryGetProperty("Description", out var desc) ? desc.GetString() ?? "" : "",
            CreationTime = element.TryGetProperty("CreationTime", out var time) ? DateTime.Parse(time.GetString() ?? "") : DateTime.MinValue,
            RestorePointType = element.TryGetProperty("RestorePointType", out var type) ? type.GetString() ?? "" : ""
        };
    }

    // ==================== RESTORE OPERATIONS ====================

    public async Task<BackupResult> RestoreBackupAsync(BackupArchive archive, string destinationPath, RestoreOptions options, IProgress<BackupProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.Now;
        var stopwatch = Stopwatch.StartNew();
        var processedFiles = 0;
        var errors = new List<BackupError>();

        try
        {
            var sourcePath = archive.FilePath;

            // Decrypt if needed
            if (archive.IsEncrypted)
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = "Encrypted backups require password - use DecryptBackup first"
                };
            }

            // Extract if compressed
            string extractPath;
            if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                extractPath = Path.Combine(Path.GetTempPath(), $"restore_{Guid.NewGuid()}");
                progress?.Report(new BackupProgress
                {
                    Status = BackupStatus.Running,
                    CurrentOperation = "Extracting backup archive..."
                });
                ZipFile.ExtractToDirectory(sourcePath, extractPath);
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
            var totalFiles = options.SelectiveFiles?.Count ?? manifest.Files.Count;
            var filesToRestore = options.SelectiveFiles != null
                ? manifest.Files.Where(f => options.SelectiveFiles.Contains(f.RelativePath)).ToList()
                : manifest.Files;

            foreach (var fileEntry in filesToRestore)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var sourceFile = Path.Combine(extractPath, fileEntry.RelativePath);
                    var destFile = options.RestoreToOriginalLocation
                        ? fileEntry.OriginalPath
                        : Path.Combine(destinationPath, fileEntry.RelativePath);

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

            // Cleanup temp extraction
            if (sourcePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                try { Directory.Delete(extractPath, true); } catch { }
            }

            stopwatch.Stop();

            return new BackupResult
            {
                Success = true,
                Status = errors.Count > 0 ? BackupStatus.PartialSuccess : BackupStatus.Completed,
                Message = errors.Count > 0 ? $"Restored with {errors.Count} errors" : "Restore completed successfully",
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
    }

    // ==================== BACKUP MANAGEMENT ====================

    public async Task<List<BackupArchive>> GetBackupHistoryAsync(string? backupLocation = null)
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
                            if (File.Exists(archive.FilePath) || Directory.Exists(archive.FilePath))
                            {
                                archives.Add(archive);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return archives.OrderByDescending(a => a.CreatedDate).ToList();
        });
    }

    public async Task<BackupResult> VerifyBackupAsync(BackupArchive archive, IProgress<BackupProgress>? progress = null)
    {
        return await Task.Run(async () =>
        {
            try
            {
                if (archive.Manifest == null)
                {
                    return new BackupResult
                    {
                        Success = false,
                        Status = BackupStatus.Failed,
                        Message = "No manifest available for verification"
                    };
                }

                var verified = 0;
                var failed = 0;

                foreach (var file in archive.Manifest.Files)
                {
                    if (!string.IsNullOrEmpty(file.Hash))
                    {
                        var currentHash = await CalculateFileHashAsync(file.OriginalPath);
                        if (currentHash == file.Hash)
                        {
                            verified++;
                        }
                        else
                        {
                            failed++;
                        }
                    }
                }

                return new BackupResult
                {
                    Success = failed == 0,
                    Status = failed == 0 ? BackupStatus.Completed : BackupStatus.PartialSuccess,
                    Message = $"Verified {verified} files, {failed} mismatches",
                    ProcessedFiles = verified,
                    FailedFiles = failed
                };
            }
            catch (Exception ex)
            {
                return new BackupResult
                {
                    Success = false,
                    Status = BackupStatus.Failed,
                    Message = $"Verification failed: {ex.Message}"
                };
            }
        });
    }

    public async Task<BackupResult> DeleteBackupAsync(BackupArchive archive)
    {
        return await Task.Run(() =>
        {
            try
            {
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
                catch { }
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
                    var dirFiles = new DirectoryInfo(sourcePath)
                        .EnumerateFiles("*", SearchOption.AllDirectories)
                        .Where(f => ShouldIncludeFile(f, job));

                    files.AddRange(dirFiles);
                }
            }

            return files;
        }, cancellationToken);
    }

    private static bool ShouldIncludeFile(FileInfo file, BackupJob job)
    {
        // Check exclusion paths
        if (job.ExcludePaths.Any(p => file.FullName.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
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

    private static List<FileInfo> FilterChangedFiles(List<FileInfo> files, BackupManifest lastBackup)
    {
        var lastBackupFiles = lastBackup.Files.ToDictionary(f => f.OriginalPath, f => f.ModifiedDate);

        return files.Where(f =>
        {
            if (!lastBackupFiles.TryGetValue(f.FullName, out var lastModified))
                return true; // New file
            return f.LastWriteTime > lastModified; // Changed file
        }).ToList();
    }

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

    private static async Task EncryptFileAsync(string inputPath, string outputPath, string password, CancellationToken cancellationToken)
    {
        var key = DeriveKey(password);
        var iv = RandomNumberGenerator.GetBytes(16);

        await using var inputStream = File.OpenRead(inputPath);
        await using var outputStream = File.Create(outputPath);

        // Write IV first
        await outputStream.WriteAsync(iv, cancellationToken);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        await using var cryptoStream = new CryptoStream(outputStream, aes.CreateEncryptor(), CryptoStreamMode.Write);
        await inputStream.CopyToAsync(cryptoStream, cancellationToken);
    }

    private static byte[] DeriveKey(string password)
    {
        var salt = Encoding.UTF8.GetBytes("SysMonitorBackup2024");
        using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32);
    }

    private async Task SaveManifestAsync(BackupManifest manifest, string path)
    {
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
    }

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

    private async Task<BackupManifest?> GetLastBackupManifestAsync(BackupJob job)
    {
        var archives = await GetBackupHistoryAsync();
        var lastBackup = archives
            .Where(a => a.Name == job.Name && a.Type == BackupType.Full)
            .OrderByDescending(a => a.CreatedDate)
            .FirstOrDefault();

        return lastBackup?.Manifest;
    }

    private async Task CleanupOldBackupsAsync(BackupJob job)
    {
        var archives = await GetBackupHistoryAsync();
        var jobArchives = archives
            .Where(a => a.Name == job.Name)
            .OrderByDescending(a => a.CreatedDate)
            .ToList();

        // Remove old backups beyond retention count
        var toDelete = jobArchives.Skip(job.MaxBackupsToKeep);
        foreach (var archive in toDelete)
        {
            await DeleteBackupAsync(archive);
        }

        // Remove backups older than max age
        var cutoffDate = DateTime.Now.AddDays(-job.MaxAgeDays);
        var oldBackups = jobArchives.Where(a => a.CreatedDate < cutoffDate);
        foreach (var archive in oldBackups)
        {
            await DeleteBackupAsync(archive);
        }
    }
}
