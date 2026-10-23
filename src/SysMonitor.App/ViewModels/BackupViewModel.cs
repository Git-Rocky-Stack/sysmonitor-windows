using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Backup;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;

namespace SysMonitor.App.ViewModels;

public partial class BackupViewModel : ObservableObject, IDisposable
{
    private readonly ILogger _logger;

    private readonly IBackupService _backupService;
    private DispatcherQueue? _dispatcherQueue;
    private CancellationTokenSource? _backupCts;

    /// <summary>Cancelled when the user leaves the page, so a backup it started does not outlive it.</summary>
    private readonly CancellationTokenSource _pageCts = new();

    private bool _isDisposed;

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

    // Repeat: 0 is once, now; 1 daily, 2 weekly, 3 monthly - registered with Windows Task Scheduler.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWeeklyRepeat), nameof(IsMonthlyRepeat), nameof(IsRepeating))]
    private int _repeatFrequency;
    [ObservableProperty] private int _repeatHour = 2;

    /// <summary>The hours a schedule can start at, in this PC's own time format.</summary>
    public IReadOnlyList<string> RepeatHours { get; } =
        Enumerable.Range(0, 24).Select(hour => DateTime.Today.AddHours(hour).ToString("t")).ToList();
    [ObservableProperty] private int _repeatDayOfWeek; // Sunday first, as DayOfWeek counts
    [ObservableProperty] private double _repeatDayOfMonth = 1;

    public bool IsRepeating => RepeatFrequency > 0;
    public bool IsWeeklyRepeat => RepeatFrequency == 2;
    public bool IsMonthlyRepeat => RepeatFrequency == 3;

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

    // ==================== LAST BACKUP ====================

    [ObservableProperty] private bool _hasLastBackup;
    [ObservableProperty] private string _lastBackupName = "";
    [ObservableProperty] private string _lastBackupWhen = "";

    // ==================== QUICK ACTIONS ====================

    [ObservableProperty] private long _estimatedBackupSize;
    [ObservableProperty] private string _formattedEstimatedSize = "Calculating...";

    // ==================== STATUS ====================

    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasStatusMessage;
    [ObservableProperty] private LampState _statusState = LampState.Go;

    public BackupViewModel(IBackupService backupService,
        ILogger<BackupViewModel>? logger = null)
    {
        _logger = logger ?? NullLogger<BackupViewModel>.Instance;
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LoadAvailableDrivesAsync failed");
        }
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

                var latest = history.FirstOrDefault();
                HasLastBackup = latest != null;
                LastBackupName = latest?.Name ?? "";
                LastBackupWhen = latest == null ? "" : Ago(latest.CreatedDate);
            });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LoadBackupHistoryAsync failed");
        }
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LoadRestorePointsAsync failed");
        }
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LoadSchedulesAsync failed");
        }
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

    /// <summary>Set by the page to its confirmation dialog; unset, nothing that asks first will run.</summary>
    public Confirmation? Confirm { get; set; }

    private bool IsSystemImage => SelectedBackupType == BackupType.SystemImage;

    [RelayCommand]
    private void NextStep()
    {
        // A system image is of the whole system drive, with Windows Backup's own settings: it has no sources to
        // choose and none of the file backup's options, so the wizard goes from the type to the destination, and
        // from there to the image.
        if (IsSystemImage && CurrentStep is 1 or 3)
        {
            if (CurrentStep == 1)
            {
                CurrentStep = 3;
                UpdateWizardTitle();
            }
            else
            {
                _ = StartSystemImageAsync();
            }
            return;
        }

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
            CurrentStep = IsSystemImage && CurrentStep == 3 ? 1 : CurrentStep - 1;
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
        (WizardTitle, WizardSubtitle) = IsSystemImage ? CurrentStep switch
        {
            1 => ("Create New Backup", "Step 1: Select backup type"),
            3 => ("Select Destination", "Step 2: Choose the drive the system image is saved on - not the one Windows is on"),
            5 => ("System Image in Progress", "Windows Backup is writing the image; keep the computer on"),
            6 => ("System Image", LastResult?.Success == true ? "The system image was saved" : "No system image was made"),
            _ => ("BACKUP MANAGER", "Protect your files with automated backups")
        } : CurrentStep switch
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AddSourceFolderAsync failed");
        }
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "AddSourceFilesAsync failed");
        }
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "BrowseDestinationAsync failed");
        }
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

        if (EnableEncryption && string.IsNullOrEmpty(EncryptionPassword))
        {
            ShowStatus("Enter a password to encrypt the backup", false);
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
        // Linked to the page: leaving the page cancels the backup as surely as pressing Cancel does.
        _backupCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);

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

            // Repeat: the job, as it was just run, registered with Task Scheduler. Its outcome goes under the
            // backup's, whether or not this first run went well - the schedule is its own thing.
            if (IsRepeating)
            {
                var scheduled = await _backupService.ScheduleBackupAsync(new BackupSchedule
                {
                    Name = BackupName,
                    Job = job,
                    Frequency = RepeatFrequency switch { 2 => BackupFrequency.Weekly, 3 => BackupFrequency.Monthly, _ => BackupFrequency.Daily },
                    TimeOfDay = TimeSpan.FromHours(Math.Clamp(RepeatHour, 0, 23)),
                    DayOfWeek = (DayOfWeek)Math.Clamp(RepeatDayOfWeek, 0, 6),
                    DayOfMonth = (int)Math.Clamp(RepeatDayOfMonth, 1, 31),
                });
                ResultDetails += $"\n\n{scheduled.Message}";
                await LoadSchedulesAsync();
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

    /// <summary>
    /// Plans the image, says what it will do and asks, then runs Windows Backup and shows its progress. Windows
    /// asks for administrator permission itself.
    /// </summary>
    private async Task StartSystemImageAsync()
    {
        var plan = await _backupService.PrepareSystemImageAsync(DestinationPath);
        if (plan.Problem != null)
        {
            ShowStatus(plan.Problem, false);
            return;
        }

        var space = plan.IsNetworkTarget
            ? "A network folder keeps only the newest image."
            : $"The first image needs up to about {FormatSize(plan.SystemUsedBytes)}; {plan.Target} has {FormatSize(plan.TargetFreeBytes)} free." +
              (plan.MayRunOutOfSpace ? " It may not fit." : " Later images add only what changed, and Windows removes the oldest when space runs short.");
        var message =
            $"Windows Backup will save an image of {plan.SystemDrive} and the partitions Windows starts from in {plan.ImageFolder}.\n\n" +
            $"{space}\n\n" +
            "Windows asks for administrator permission next. It can take an hour or more; keep the computer on.\n\n" +
            "To put the image back, start Windows Recovery and choose System Image Recovery. It replaces everything on those drives.";
        if (!await Confirm.AskAsync("Create a system image?", message, "Create image"))
            return;

        CurrentStep = 5;
        UpdateWizardTitle();
        IsBackupRunning = true;
        ProgressPercent = 0;
        ProgressSpeed = "";
        ProgressEta = "";
        ProgressStatus = "Preparing system image...";
        _backupCts = CancellationTokenSource.CreateLinkedTokenSource(_pageCts.Token);

        var progress = new Progress<BackupProgress>(p =>
        {
            GetDispatcher().TryEnqueue(() =>
            {
                ProgressPercent = p.PercentComplete;
                ProgressStatus = p.CurrentOperation;
                CurrentFile = p.CurrentFile;
                ProcessedInfo = $"{p.ProcessedBytes}% - {p.Elapsed:hh\\:mm\\:ss} so far";
            });
        });

        try
        {
            LastResult = await _backupService.CreateSystemImageAsync(DestinationPath, progress, _backupCts.Token);
            HasResult = true;
            ResultSuccess = LastResult.Success;
            ResultMessage = LastResult.Success ? "System image saved" : "No system image was made";
            ResultDetails = LastResult.Success
                ? $"Duration: {LastResult.Duration:hh\\:mm\\:ss}\nSaved to: {LastResult.OutputPath}\n\nTo restore it: {SystemImageRestoreSteps}"
                : LastResult.Message;

            CurrentStep = 6;
            UpdateWizardTitle();
            await LoadBackupHistoryAsync();
        }
        catch (Exception ex)
        {
            ShowStatus($"System image error: {ex.Message}", false);
        }
        finally
        {
            IsBackupRunning = false;
            _backupCts?.Dispose();
            _backupCts = null;
        }
    }

    private const string SystemImageRestoreSteps =
        "Settings > System > Recovery > Advanced startup > Restart now, then Troubleshoot > Advanced options > System Image Recovery.";

    // ==================== SCHEDULES ====================

    /// <summary>Runs a schedule's backup now, here, with its progress on screen.</summary>
    [RelayCommand]
    private async Task RunScheduleNowAsync(BackupScheduleViewModel schedule)
    {
        if (schedule == null || IsBackupRunning) return;

        IsBackupRunning = true;
        ProgressStatus = $"Running \"{schedule.Name}\"...";
        try
        {
            var result = await _backupService.RunScheduledBackupAsync(schedule.Schedule.Id);
            ShowStatus(result.Message, result.Success);
            await Task.WhenAll(LoadSchedulesAsync(), LoadBackupHistoryAsync());
        }
        finally
        {
            IsBackupRunning = false;
        }
    }

    /// <summary>Removes a schedule and its task; the backups it already made stay.</summary>
    [RelayCommand]
    private async Task DeleteScheduleAsync(BackupScheduleViewModel schedule)
    {
        if (schedule == null) return;

        if (!await Confirm.AskAsync("Stop this schedule?",
                $"\"{schedule.Name}\" will no longer run {schedule.FrequencyDisplay}. Its task leaves Task Scheduler. The backups it already made stay.",
                "Stop schedule"))
            return;

        var removed = await _backupService.RemoveScheduledBackupAsync(schedule.Schedule.Id);
        ShowStatus(removed ? $"\"{schedule.Name}\" no longer repeats" : $"\"{schedule.Name}\" could not be removed; Task Scheduler may still run it", removed);
        if (removed)
        {
            Schedules.Remove(schedule);
            HasSchedules = Schedules.Count > 0;
        }
    }

    private static string Ago(DateTime when)
    {
        var age = DateTime.Now - when;
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? $"{(int)age.TotalMinutes} minute(s) ago"
            : age.TotalDays < 1 ? $"{(int)age.TotalHours} hour(s) ago"
            : age.TotalDays < 30 ? $"{(int)age.TotalDays} day(s) ago"
            : when.ToString("d");
    }

    // ==================== BACKUP HISTORY ACTIONS ====================

    /// <summary>
    /// Asked before a restore writes anything, with what it would write and where. The page puts it on
    /// screen; without an answer, nothing is written.
    /// </summary>
    public Func<RestorePlan, Task<bool>>? ConfirmRestore { get; set; }

    [RelayCommand]
    private Task RestoreBackupAsync(BackupArchiveViewModel archive) => RestoreBackupWithPasswordAsync(archive, null);

    /// <summary>
    /// Restores a backup; <paramref name="password"/> is required for encrypted backups
    /// (the page prompts for it before calling this).
    /// </summary>
    public async Task RestoreBackupWithPasswordAsync(BackupArchiveViewModel archive, string? password)
    {
        if (archive?.Archive == null) return;

        // Windows puts a system image back from outside Windows; the app can only say how.
        if (archive.Archive.Type == BackupType.SystemImage)
        {
            ShowStatus($"To restore a system image: {SystemImageRestoreSteps}", true);
            return;
        }

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

            var options = new RestoreOptions
            {
                RestoreToOriginalLocation = false,
                AlternateDestination = folder.Path,
                OverwriteExisting = true,
                Password = password
            };

            // What this would write, worked out before anything is written, and shown before it happens.
            ProgressStatus = "Checking what this backup would restore...";
            var plan = await _backupService.PrepareRestoreAsync(archive.Archive, folder.Path, options);

            if (plan.Files.Count == 0)
            {
                ShowStatus(plan.Problem ?? (plan.Refused.Count > 0
                    ? $"Nothing was restored: every entry in this backup ({plan.Refused.Count}) asked to be written somewhere it does not belong."
                    : "This backup has nothing to restore."), false);
                return;
            }

            if (ConfirmRestore != null && !await ConfirmRestore(plan))
            {
                ShowStatus("Nothing was restored", false);
                return;
            }

            IsBackupRunning = true;
            ProgressStatus = "Restoring backup...";

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
    private Task VerifyBackupAsync(BackupArchiveViewModel archive) => VerifyBackupWithPasswordAsync(archive, null);

    /// <summary>
    /// Verifies a backup's contents against its recorded checksums; <paramref name="password"/> is required
    /// for encrypted backups (the page prompts for it before calling this).
    /// </summary>
    public async Task VerifyBackupWithPasswordAsync(BackupArchiveViewModel archive, string? password)
    {
        if (archive?.Archive == null) return;

        if (archive.Archive.Type == BackupType.SystemImage)
        {
            ShowStatus("Windows Backup checked this system image as it wrote it; its folder is closed to the app.", true);
            return;
        }

        IsBackupRunning = true;
        ProgressStatus = "Verifying backup...";

        try
        {
            var result = await _backupService.VerifyBackupAsync(archive.Archive, password: password);
            ShowStatus(result.Message, result.Success);
        }
        finally
        {
            IsBackupRunning = false;
        }
    }

    // ==================== HELPERS ====================

    private void ShowStatus(string message, bool isSuccess)
    {
        StatusMessage = message;
        StatusState = isSuccess ? LampState.Go : LampState.NoGo;
        HasStatusMessage = true;
        // Good news fades; a failure stays until the next message replaces it, and a timer started for an older
        // message never clears a newer one (as LargeFilesViewModel does).
        var shown = ++_statusShown;
        if (isSuccess)
            _ = ClearStatusAfterDelayAsync(shown);
    }

    private int _statusShown;

    private async Task ClearStatusAfterDelayAsync(int shown)
    {
        await Task.Delay(5000);
        GetDispatcher().TryEnqueue(() =>
        {
            if (shown == _statusShown)
                HasStatusMessage = false;
        });
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

    /// <summary>
    /// Ends a backup the user has walked away from, and releases what it was using. Without this the copy
    /// loop carried on writing while the page that showed its progress was gone, and the wizard was left
    /// stranded on its last step.
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _pageCts.Cancel();
        _pageCts.Dispose();
        _backupCts?.Dispose();
        _backupCts = null;

        GC.SuppressFinalize(this);
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
    public bool IsSystemImage => Archive.Type == BackupType.SystemImage;
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
    public string FrequencyDisplay => BackupService.Describe(Schedule);
    public string NextRunDisplay => Schedule.NextRunTime is { } next ? $"Next: {next:g}" : "Not scheduled";
    public string LastRunDisplay => Schedule.LastRunTime is { } last
        ? $"Last: {last:g} - {(Schedule.LastResult?.Success == true ? "completed" : "did not complete")}"
        : "Not run yet";
    public string Destination => Schedule.Job.DestinationPath;

    public BackupScheduleViewModel(BackupSchedule schedule)
    {
        Schedule = schedule;
    }
}
