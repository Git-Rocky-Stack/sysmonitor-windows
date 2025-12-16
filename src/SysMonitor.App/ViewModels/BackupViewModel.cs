using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Backup;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;

namespace SysMonitor.App.ViewModels;

public partial class BackupViewModel : ObservableObject
{
    private readonly IBackupService _backupService;
    private DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _backupCts;

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    // ==================== WIZARD STATE ====================

    [ObservableProperty] private int _currentStep = 0; // 0=Home, 1=SelectType, 2=SelectSource, 3=SelectDest, 4=Options, 5=Running, 6=Complete
    [ObservableProperty] private bool _isWizardMode;
    [ObservableProperty] private string _wizardTitle = "BACKUP MANAGER";
    [ObservableProperty] private string _wizardSubtitle = "Protect your files with automated backups";

    // ==================== BACKUP JOB CONFIG ====================

    [ObservableProperty] private string _backupName = "My Backup";
    [ObservableProperty] private BackupType _selectedBackupType = BackupType.Full;
    [ObservableProperty] private BackupDestinationType _selectedDestinationType = BackupDestinationType.LocalDrive;
    [ObservableProperty] private BackupCompression _selectedCompression = BackupCompression.Normal;

    // Source paths
    public ObservableCollection<string> SourcePaths { get; } = [];
    public ObservableCollection<string> ExcludePaths { get; } = [];

    // Destination
    [ObservableProperty] private string _destinationPath = "";
    [ObservableProperty] private DriveSpaceInfo? _selectedDrive;
    public ObservableCollection<DriveDisplayInfo> AvailableDrives { get; } = [];

    // Options
    [ObservableProperty] private bool _enableEncryption;
    [ObservableProperty] private string _encryptionPassword = "";
    [ObservableProperty] private string _confirmPassword = "";
    [ObservableProperty] private bool _verifyAfterBackup = true;
    [ObservableProperty] private bool _useVss = true;
    [ObservableProperty] private bool _includeHiddenFiles;
    [ObservableProperty] private int _maxBackupsToKeep = 5;

    // ==================== PROGRESS STATE ====================

    [ObservableProperty] private bool _isBackupRunning;
    [ObservableProperty] private double _progressPercent;
    [ObservableProperty] private string _progressStatus = "";
    [ObservableProperty] private string _currentFile = "";
    [ObservableProperty] private string _progressSpeed = "";
    [ObservableProperty] private string _progressEta = "";
    [ObservableProperty] private string _processedInfo = "";
    [ObservableProperty] private long _totalBytes;
    [ObservableProperty] private long _processedBytes;

    // ==================== RESULT STATE ====================

    [ObservableProperty] private BackupResult? _lastResult;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _resultMessage = "";
    [ObservableProperty] private string _resultDetails = "";
    [ObservableProperty] private bool _resultSuccess;

    // ==================== BACKUP HISTORY ====================

    public ObservableCollection<BackupArchiveViewModel> BackupHistory { get; } = [];
    [ObservableProperty] private BackupArchiveViewModel? _selectedBackup;
    [ObservableProperty] private bool _hasBackups;

    // ==================== RESTORE POINTS ====================

    public ObservableCollection<RestorePointInfo> RestorePoints { get; } = [];
    [ObservableProperty] private bool _hasRestorePoints;

    // ==================== SCHEDULES ====================

    public ObservableCollection<BackupScheduleViewModel> Schedules { get; } = [];
    [ObservableProperty] private bool _hasSchedules;

    // ==================== QUICK ACTIONS ====================

    [ObservableProperty] private long _estimatedBackupSize;
    [ObservableProperty] private string _formattedEstimatedSize = "Calculating...";

    // ==================== STATUS ====================

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasStatusMessage;
    [ObservableProperty] private string _statusColor = "#4CAF50";

    public BackupViewModel(IBackupService backupService)
    {
        _backupService = backupService;

        // Add default exclusion paths
        ExcludePaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows)));
        ExcludePaths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"));
        ExcludePaths.Add("$Recycle.Bin");
    }

    private DispatcherQueue GetDispatcher()
    {
        _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();
        return _dispatcherQueue;
    }

    // ==================== INITIALIZATION ====================

    [RelayCommand]
    private async Task LoadDataAsync()
    {
        await Task.WhenAll(
            LoadAvailableDrivesAsync(),
            LoadBackupHistoryAsync(),
            LoadRestorePointsAsync(),
            LoadSchedulesAsync()
        );
    }

    private async Task LoadAvailableDrivesAsync()
    {
        try
        {
            var drives = await _backupService.GetAvailableDrivesAsync();
            GetDispatcher().TryEnqueue(() =>
            {
                AvailableDrives.Clear();
                foreach (var drive in drives)
                {
                    if (drive.IsReady)
                    {
                        AvailableDrives.Add(new DriveDisplayInfo
                        {
                            Path = drive.Name,
                            Label = string.IsNullOrEmpty(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                            DriveType = drive.DriveType.ToString(),
                            TotalSize = drive.TotalSize,
                            FreeSpace = drive.AvailableFreeSpace,
                            IsRemovable = drive.DriveType == DriveType.Removable,
                            Icon = drive.DriveType == DriveType.Removable ? "\uE88E" : "\uEDA2"
                        });
                    }
                }
            });
        }
        catch { }
    }

    private async Task LoadBackupHistoryAsync()
    {
        try
        {
            var history = await _backupService.GetBackupHistoryAsync();
            GetDispatcher().TryEnqueue(() =>
            {
                BackupHistory.Clear();
                foreach (var archive in history.Take(20))
                {
                    BackupHistory.Add(new BackupArchiveViewModel(archive));
                }
                HasBackups = BackupHistory.Count > 0;
            });
        }
        catch { }
    }

    private async Task LoadRestorePointsAsync()
    {
        try
        {
            var points = await _backupService.GetRestorePointsAsync();
            GetDispatcher().TryEnqueue(() =>
            {
                RestorePoints.Clear();
                foreach (var point in points.Take(10))
                {
                    RestorePoints.Add(point);
                }
                HasRestorePoints = RestorePoints.Count > 0;
            });
        }
        catch { }
    }

    private async Task LoadSchedulesAsync()
    {
        try
        {
            var schedules = await _backupService.GetScheduledBackupsAsync();
            GetDispatcher().TryEnqueue(() =>
            {
                Schedules.Clear();
                foreach (var schedule in schedules)
                {
                    Schedules.Add(new BackupScheduleViewModel(schedule));
                }
                HasSchedules = Schedules.Count > 0;
            });
        }
        catch { }
    }

    // ==================== WIZARD NAVIGATION ====================

    [RelayCommand]
    private void StartNewBackup()
    {
        IsWizardMode = true;
        CurrentStep = 1;
        WizardTitle = "Create New Backup";
        WizardSubtitle = "Step 1: Select backup type";
        SourcePaths.Clear();
        DestinationPath = "";
        BackupName = $"Backup_{DateTime.Now:yyyyMMdd}";
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep < 5)
        {
            CurrentStep++;
            UpdateWizardTitle();
        }

        if (CurrentStep == 5)
        {
            _ = StartBackupAsync();
        }
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
            UpdateWizardTitle();
        }
    }

    [RelayCommand]
    private void CancelWizard()
    {
        IsWizardMode = false;
        CurrentStep = 0;
        WizardTitle = "BACKUP MANAGER";
        WizardSubtitle = "Protect your files with automated backups";
    }

    private void UpdateWizardTitle()
    {
        (WizardTitle, WizardSubtitle) = CurrentStep switch
        {
            1 => ("Create New Backup", "Step 1: Select backup type"),
            2 => ("Select Source", "Step 2: Choose files and folders to backup"),
            3 => ("Select Destination", "Step 3: Choose where to save your backup"),
            4 => ("Backup Options", "Step 4: Configure backup settings"),
            5 => ("Backup in Progress", "Please wait while your files are backed up"),
            6 => ("Backup Complete", LastResult?.Success == true ? "Your backup was successful!" : "Backup completed with errors"),
            _ => ("BACKUP MANAGER", "Protect your files with automated backups")
        };
    }

    // ==================== SOURCE SELECTION ====================

    [RelayCommand]
    private async Task AddSourceFolderAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null && !SourcePaths.Contains(folder.Path))
            {
                SourcePaths.Add(folder.Path);
                await UpdateEstimatedSizeAsync();
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task AddSourceFilesAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var files = await picker.PickMultipleFilesAsync();
            foreach (var file in files)
            {
                if (!SourcePaths.Contains(file.Path))
                {
                    SourcePaths.Add(file.Path);
                }
            }
            await UpdateEstimatedSizeAsync();
        }
        catch { }
    }

    [RelayCommand]
    private void AddQuickSource(string sourceType)
    {
        var path = sourceType switch
        {
            "Documents" => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "Pictures" => Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            "Videos" => Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            "Music" => Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            "Desktop" => Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "Downloads" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            _ => null
        };

        if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && !SourcePaths.Contains(path))
        {
            SourcePaths.Add(path);
            _ = UpdateEstimatedSizeAsync();
        }
    }

    [RelayCommand]
    private void RemoveSourcePath(string path)
    {
        SourcePaths.Remove(path);
        _ = UpdateEstimatedSizeAsync();
    }

    private async Task UpdateEstimatedSizeAsync()
    {
        if (SourcePaths.Count == 0)
        {
            FormattedEstimatedSize = "No files selected";
            EstimatedBackupSize = 0;
            return;
        }

        FormattedEstimatedSize = "Calculating...";

        try
        {
            var job = CreateBackupJob();
            EstimatedBackupSize = await _backupService.EstimateBackupSizeAsync(job);
            FormattedEstimatedSize = FormatSize(EstimatedBackupSize);
        }
        catch
        {
            FormattedEstimatedSize = "Unable to calculate";
        }
    }

    // ==================== DESTINATION SELECTION ====================

    [RelayCommand]
    private void SelectDrive(DriveDisplayInfo drive)
    {
        DestinationPath = drive.Path;
        SelectedDrive = new DriveSpaceInfo
        {
            DrivePath = drive.Path,
            DriveLabel = drive.Label,
            TotalBytes = drive.TotalSize,
            FreeBytes = drive.FreeSpace,
            IsRemovable = drive.IsRemovable
        };
    }

    [RelayCommand]
    private async Task BrowseDestinationAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                DestinationPath = folder.Path;
                SelectedDrive = await _backupService.GetDriveSpaceAsync(folder.Path);
            }
        }
        catch { }
    }

    // ==================== BACKUP EXECUTION ====================

    private BackupJob CreateBackupJob()
    {
        return new BackupJob
        {
            Name = BackupName,
            Type = SelectedBackupType,
            DestinationType = SelectedDestinationType,
            SourcePaths = SourcePaths.ToList(),
            ExcludePaths = ExcludePaths.ToList(),
            DestinationPath = DestinationPath,
            Compression = SelectedCompression,
            EnableEncryption = EnableEncryption,
            EncryptionPassword = EnableEncryption ? EncryptionPassword : null,
            VerifyAfterBackup = VerifyAfterBackup,
            UseVss = UseVss,
            IncludeHiddenFiles = IncludeHiddenFiles,
            MaxBackupsToKeep = MaxBackupsToKeep
        };
    }

    private async Task StartBackupAsync()
    {
        if (SourcePaths.Count == 0 || string.IsNullOrEmpty(DestinationPath))
        {
            ShowStatus("Please select source files and destination", false);
            return;
        }

        if (EnableEncryption && EncryptionPassword != ConfirmPassword)
        {
            ShowStatus("Passwords do not match", false);
            return;
        }

        IsBackupRunning = true;
        ProgressPercent = 0;
        ProgressStatus = "Preparing backup...";
        _backupCts = new CancellationTokenSource();

        var progress = new Progress<BackupProgress>(p =>
        {
            GetDispatcher().TryEnqueue(() =>
            {
                ProgressPercent = p.PercentComplete;
                ProgressStatus = p.CurrentOperation;
                CurrentFile = p.CurrentFile;
                ProgressSpeed = p.FormattedSpeed;
                ProcessedBytes = p.ProcessedBytes;
                TotalBytes = p.TotalBytes;
                ProcessedInfo = $"{p.ProcessedFiles:N0} of {p.TotalFiles:N0} files ({FormatSize(p.ProcessedBytes)} of {FormatSize(p.TotalBytes)})";

                if (p.EstimatedTimeRemaining.HasValue)
                {
                    var eta = p.EstimatedTimeRemaining.Value;
                    ProgressEta = eta.TotalHours >= 1
                        ? $"{(int)eta.TotalHours}h {eta.Minutes}m remaining"
                        : eta.TotalMinutes >= 1
                            ? $"{(int)eta.TotalMinutes}m {eta.Seconds}s remaining"
                            : $"{eta.Seconds}s remaining";
                }
            });
        });

        try
        {
            var job = CreateBackupJob();
            LastResult = await _backupService.CreateBackupAsync(job, progress, _backupCts.Token);
            HasResult = true;
            ResultSuccess = LastResult.Success;
            ResultMessage = LastResult.Message;
            ResultDetails = $"Duration: {LastResult.Duration:hh\\:mm\\:ss}\n" +
                           $"Files: {LastResult.ProcessedFiles:N0} backed up, {LastResult.FailedFiles:N0} failed\n" +
                           $"Size: {FormatSize(LastResult.ProcessedBytes)}";

            if (!string.IsNullOrEmpty(LastResult.OutputPath))
            {
                ResultDetails += $"\nSaved to: {LastResult.OutputPath}";
            }

            CurrentStep = 6;
            UpdateWizardTitle();

            await LoadBackupHistoryAsync();
        }
        catch (Exception ex)
        {
            ShowStatus($"Backup error: {ex.Message}", false);
        }
        finally
        {
            IsBackupRunning = false;
            _backupCts?.Dispose();
            _backupCts = null;
        }
    }

    [RelayCommand]
    private void CancelBackup()
    {
        _backupCts?.Cancel();
        ProgressStatus = "Cancelling...";
    }

    // ==================== QUICK ACTIONS ====================

    [RelayCommand]
    private async Task QuickBackupDocumentsAsync()
    {
        SourcePaths.Clear();
        SourcePaths.Add(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        BackupName = "Documents_Backup";
        SelectedBackupType = BackupType.Full;
        SelectedCompression = BackupCompression.Normal;

        // Auto-select first available drive with enough space
        await LoadAvailableDrivesAsync();
        var drive = AvailableDrives.FirstOrDefault(d => d.FreeSpace > 1_000_000_000); // 1GB minimum
        if (drive != null)
        {
            SelectDrive(drive);
            StartNewBackup();
            CurrentStep = 4; // Skip to options
            UpdateWizardTitle();
        }
        else
        {
            ShowStatus("No suitable destination drive found", false);
        }
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        var description = $"SysMonitor Restore Point - {DateTime.Now:g}";
        var result = await _backupService.CreateRestorePointAsync(description);

        ShowStatus(result.Message, result.Success);

        if (result.Success)
        {
            await LoadRestorePointsAsync();
        }
    }

    [RelayCommand]
    private async Task CreateSystemImageAsync()
    {
        if (string.IsNullOrEmpty(DestinationPath))
        {
            ShowStatus("Please select a destination drive first", false);
            return;
        }

        IsBackupRunning = true;
        ProgressStatus = "Creating system image...";
        ProgressPercent = 0;

        var progress = new Progress<BackupProgress>(p =>
        {
            GetDispatcher().TryEnqueue(() =>
            {
                ProgressStatus = p.CurrentOperation;
            });
        });

        var result = await _backupService.CreateSystemImageAsync(DestinationPath, progress);

        IsBackupRunning = false;
        ShowStatus(result.Message, result.Success);
    }

    // ==================== BACKUP HISTORY ACTIONS ====================

    [RelayCommand]
    private async Task RestoreBackupAsync(BackupArchiveViewModel archive)
    {
        if (archive?.Archive == null) return;

        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add("*");

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            IsBackupRunning = true;
            ProgressStatus = "Restoring backup...";

            var options = new RestoreOptions
            {
                RestoreToOriginalLocation = false,
                AlternateDestination = folder.Path,
                OverwriteExisting = true
            };

            var progress = new Progress<BackupProgress>(p =>
            {
                GetDispatcher().TryEnqueue(() =>
                {
                    ProgressPercent = p.FilesPercentComplete;
                    ProgressStatus = p.CurrentOperation;
                    CurrentFile = p.CurrentFile;
                });
            });

            var result = await _backupService.RestoreBackupAsync(archive.Archive, folder.Path, options, progress);

            IsBackupRunning = false;
            ShowStatus(result.Message, result.Success);
        }
        catch (Exception ex)
        {
            IsBackupRunning = false;
            ShowStatus($"Restore error: {ex.Message}", false);
        }
    }

    [RelayCommand]
    private async Task DeleteBackupAsync(BackupArchiveViewModel archive)
    {
        if (archive?.Archive == null) return;

        var result = await _backupService.DeleteBackupAsync(archive.Archive);
        ShowStatus(result.Message, result.Success);

        if (result.Success)
        {
            BackupHistory.Remove(archive);
            HasBackups = BackupHistory.Count > 0;
        }
    }

    [RelayCommand]
    private async Task VerifyBackupAsync(BackupArchiveViewModel archive)
    {
        if (archive?.Archive == null) return;

        IsBackupRunning = true;
        ProgressStatus = "Verifying backup...";

        var result = await _backupService.VerifyBackupAsync(archive.Archive);

        IsBackupRunning = false;
        ShowStatus(result.Message, result.Success);
    }

    // ==================== HELPERS ====================

    private void ShowStatus(string message, bool isSuccess)
    {
        StatusMessage = message;
        StatusColor = isSuccess ? "#4CAF50" : "#F44336";
        HasStatusMessage = true;
        _ = ClearStatusAfterDelayAsync();
    }

    private async Task ClearStatusAfterDelayAsync()
    {
        await Task.Delay(5000);
        GetDispatcher().TryEnqueue(() => HasStatusMessage = false);
    }

    private static string FormatSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F1} {sizes[order]}";
    }
}

// ==================== SUPPORTING VIEW MODELS ====================

public partial class DriveDisplayInfo : ObservableObject
{
    public string Path { get; set; } = "";
    public string Label { get; set; } = "";
    public string DriveType { get; set; } = "";
    public long TotalSize { get; set; }
    public long FreeSpace { get; set; }
    public bool IsRemovable { get; set; }
    public string Icon { get; set; } = "\uEDA2";
    public string FormattedTotal => FormatSize(TotalSize);
    public string FormattedFree => FormatSize(FreeSpace);
    public double UsedPercent => TotalSize > 0 ? ((TotalSize - FreeSpace) * 100.0 / TotalSize) : 0;

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_000_000_000_000) return $"{bytes / 1_000_000_000_000.0:F1} TB";
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000.0:F1} GB";
        if (bytes >= 1_000_000) return $"{bytes / 1_000_000.0:F1} MB";
        return $"{bytes / 1_000.0:F1} KB";
    }
}

public partial class BackupArchiveViewModel : ObservableObject
{
    public BackupArchive Archive { get; }

    public string Name => Archive.Name;
    public string FilePath => Archive.FilePath;
    public string FormattedSize => Archive.FormattedSize;
    public string FormattedDate => Archive.CreatedDate.ToString("g");
    public int FileCount => Archive.FileCount;
    public string TypeDisplay => Archive.Type.ToString();
    public bool IsEncrypted => Archive.IsEncrypted;
    public string Icon => Archive.Type switch
    {
        BackupType.Full => "\uE8F1",
        BackupType.Incremental => "\uE8B2",
        BackupType.SystemImage => "\uE770",
        _ => "\uE8B7"
    };

    public BackupArchiveViewModel(BackupArchive archive)
    {
        Archive = archive;
    }
}

public partial class BackupScheduleViewModel : ObservableObject
{
    public BackupSchedule Schedule { get; }

    public string Name => Schedule.Name;
    public bool IsEnabled => Schedule.IsEnabled;
    public string FrequencyDisplay => Schedule.Frequency.ToString();
    public string NextRunDisplay => Schedule.NextRunTime?.ToString("g") ?? "Not scheduled";
    public string LastRunDisplay => Schedule.LastRunTime?.ToString("g") ?? "Never";

    public BackupScheduleViewModel(BackupSchedule schedule)
    {
        Schedule = schedule;
    }
}
