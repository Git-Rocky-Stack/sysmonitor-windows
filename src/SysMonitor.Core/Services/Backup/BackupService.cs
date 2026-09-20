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
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SysMonitor", "Backups"))
    {
    }

    /// <summary>Uses <paramref name="metadataFolder"/> for the backup catalog (tests use an isolated folder).</summary>
    internal BackupService(string metadataFolder)
    {
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
                SourceRoots = job.SourcePaths.ToList(),
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
                await BackupEncryption.EncryptFileAsync(finalBackupPath, encryptedPath, job.EncryptionPassword, _currentBackupCts.Token);
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
            var totalFiles = options.SelectiveFiles?.Count ?? manifest.Files.Count;
            var filesToRestore = options.SelectiveFiles != null
                ? manifest.Files.Where(f => options.SelectiveFiles.Contains(f.RelativePath)).ToList()
                : manifest.Files;

            var plan = PlanRestore(manifest, filesToRestore, extractPath, destinationPath, options);
            errors.AddRange(plan.Refused);
            totalFiles = plan.Files.Count;

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

            return new BackupResult
            {
                Success = true,
                Status = errors.Count > 0 ? BackupStatus.PartialSuccess : BackupStatus.Completed,
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
                try { Directory.Delete(tempExtractPath, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
            if (decryptedArchivePath != null)
            {
                try { File.Delete(decryptedArchivePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
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
            if (archive.Manifest == null)
            {
                return Failed("No manifest available for verification");
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
            return new BackupResult
            {
                Success = ok,
                Status = ok ? BackupStatus.Completed : BackupStatus.Failed,
                Message = ok
                    ? $"Backup verified: all {verified} files match their recorded checksums"
                    : $"Backup verification failed: {problems.Count} file(s) missing or changed in the backup ({verified} verified)",
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
                try { File.Delete(decryptedArchivePath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        static BackupResult Failed(string message) => new() { Success = false, Status = BackupStatus.Failed, Message = message };
    }

    private static string NormalizeEntryName(string name) => name.Replace('\\', '/').TrimStart('/');

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
        var manifest = await ReadManifestOnlyAsync(archive, options, cancellationToken);
        if (manifest == null)
        {
            return new RestorePlan();
        }

        var entries = options.SelectiveFiles != null
            ? manifest.Files.Where(f => options.SelectiveFiles.Contains(f.RelativePath)).ToList()
            : manifest.Files;

        // Nothing is extracted for a preview, so the place files would be read from stands in for the real
        // one; it is only used to check that no entry points out of the backup.
        var notionalExtractPath = Path.Combine(Path.GetTempPath(), "sysmonitor-restore-preview");
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
                try { File.Delete(decrypted); } catch { }
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

    private static bool IsInside(string root, string candidate)
    {
        var rooted = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rooted, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(candidate.TrimEnd(Path.DirectorySeparatorChar), root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
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
