using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Monitoring;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;

namespace SysMonitor.App.ViewModels;

public partial class LargeFilesViewModel : ObservableObject, IDisposable
{
    private readonly ILargeFileFinder _largeFileFinder;
    private readonly IPerformanceMonitor _performanceMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _scanCts;
    private bool _isDisposed;

    public ObservableCollection<LargeFileDisplay> LargeFiles { get; } = [];

    // Scan Settings
    [ObservableProperty] private string _scanPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    [ObservableProperty] private int _minSizeMB = 100;

    // Stats
    [ObservableProperty] private int _filesFound;
    [ObservableProperty] private string _totalSize = "0 B";
    [ObservableProperty] private long _totalSizeBytes;

    // Progress
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _scanProgress;
    [ObservableProperty] private string _scanStatus = "Ready to scan";
    [ObservableProperty] private string _currentFile = "";

    // Action Status
    [ObservableProperty] private string _actionStatus = "";
    [ObservableProperty] private bool _hasActionStatus;
    [ObservableProperty] private string _actionStatusColor = "#4CAF50";

    // Quick Filters
    [ObservableProperty] private string _selectedFilter = "All";
    public string[] FileTypeFilters { get; } = ["All", "Video", "Image", "Audio", "Archive", "Document", "Executable", "Other"];

    public LargeFilesViewModel(ILargeFileFinder largeFileFinder, IPerformanceMonitor performanceMonitor)
    {
        _largeFileFinder = largeFileFinder;
        _performanceMonitor = performanceMonitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            picker.FileTypeFilter.Add("*");

            // Get active window handle
            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                ScanPath = folder.Path;
            }
        }
        catch
        {
            // Folder picker failed - ignore
        }
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        if (IsScanning)
        {
            _scanCts?.Cancel();
            return;
        }

        if (!Directory.Exists(ScanPath))
        {
            ShowAction("Invalid path", false);
            return;
        }

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanStatus = "Starting scan...";
        LargeFiles.Clear();
        TotalSizeBytes = 0;
        FilesFound = 0;

        using var _ = _performanceMonitor.TrackOperation("LargeFiles.Scan");

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    ScanProgress = p.PercentComplete;
                    ScanStatus = p.Status;
                    CurrentFile = p.CurrentFile;
                });
            });

            var minSizeBytes = (long)MinSizeMB * 1024 * 1024;
            var results = await _largeFileFinder.ScanAsync(ScanPath, minSizeBytes, progress, _scanCts.Token);

            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var file in results)
                {
                    LargeFiles.Add(new LargeFileDisplay(file));
                    TotalSizeBytes += file.SizeBytes;
                }

                FilesFound = LargeFiles.Count;
                TotalSize = FormatSize(TotalSizeBytes);
                ScanStatus = $"Scan complete - {FilesFound} large files found";
                ScanProgress = 100;
                ShowAction($"Found {FilesFound} files totaling {TotalSize}", true);
            });
        }
        catch (OperationCanceledException)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ScanStatus = "Scan cancelled";
                ShowAction("Scan cancelled by user", false);
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ScanStatus = "Scan failed";
                ShowAction($"Error: {ex.Message}", false);
            });
        }
        finally
        {
            _dispatcherQueue.TryEnqueue(() => IsScanning = false);
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var selected = LargeFiles.Where(f => f.IsSelected).ToList();
        if (selected.Count == 0)
        {
            ShowAction("No files selected", false);
            return;
        }

        var deletedCount = 0;
        long freedBytes = 0;

        foreach (var file in selected)
        {
            if (await _largeFileFinder.MoveToRecycleBinAsync(file.FullPath))
            {
                deletedCount++;
                freedBytes += file.SizeBytes;
                _dispatcherQueue.TryEnqueue(() =>
                {
                    LargeFiles.Remove(file);
                    TotalSizeBytes -= file.SizeBytes;
                });
            }
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            FilesFound = LargeFiles.Count;
            TotalSize = FormatSize(TotalSizeBytes);
            ShowAction($"Moved {deletedCount} files ({FormatSize(freedBytes)}) to Recycle Bin", true);
        });
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var file in LargeFiles)
            file.IsSelected = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var file in LargeFiles)
            file.IsSelected = false;
    }

    [RelayCommand]
    private void ApplyFilter()
    {
        // Filter is applied through the UI binding - this triggers refresh
    }

    partial void OnSelectedFilterChanged(string value)
    {
        // Could filter the collection here if needed
    }

    private void ShowAction(string message, bool isSuccess)
    {
        ActionStatus = message;
        ActionStatusColor = isSuccess ? "#4CAF50" : "#F44336";
        HasActionStatus = true;
        _ = ClearActionAfterDelayAsync();
    }

    private async Task ClearActionAfterDelayAsync()
    {
        await Task.Delay(5000);
        _dispatcherQueue.TryEnqueue(() => HasActionStatus = false);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_000_000_000)
            return $"{bytes / 1_000_000_000.0:F2} GB";
        if (bytes >= 1_000_000)
            return $"{bytes / 1_000_000.0:F2} MB";
        if (bytes >= 1_000)
            return $"{bytes / 1_000.0:F2} KB";
        return $"{bytes} B";
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
    }
}

public partial class LargeFileDisplay : ObservableObject
{
    public string FullPath { get; }
    public string FileName { get; }
    public string Directory { get; }
    public long SizeBytes { get; }
    public string FormattedSize { get; }
    public DateTime LastModified { get; }
    public string Extension { get; }
    public string FileType { get; }
    public string FileTypeColor { get; }
    public string FileIcon { get; }

    [ObservableProperty] private bool _isSelected;

    public LargeFileDisplay(LargeFileInfo info)
    {
        FullPath = info.FullPath;
        FileName = info.FileName;
        Directory = info.Directory;
        SizeBytes = info.SizeBytes;
        FormattedSize = info.FormattedSize;
        LastModified = info.LastModified;
        Extension = info.Extension;
        FileType = info.FileType;
        FileTypeColor = GetTypeColor(info.FileType);
        FileIcon = GetFileIcon(info.FileType);
    }

    private static string GetTypeColor(string type) => type switch
    {
        "Video" => "#E91E63",
        "Image" => "#9C27B0",
        "Audio" => "#3F51B5",
        "Archive" => "#FF9800",
        "Document" => "#2196F3",
        "Executable" => "#F44336",
        "Disk Image" => "#795548",
        "Game Data" => "#4CAF50",
        _ => "#607D8B"
    };

    private static string GetFileIcon(string type) => type switch
    {
        "Video" => "\uE8B2",
        "Image" => "\uEB9F",
        "Audio" => "\uE8D6",
        "Archive" => "\uE8B7",
        "Document" => "\uE8A5",
        "Executable" => "\uE756",
        "Disk Image" => "\uE958",
        "Game Data" => "\uE7FC",
        _ => "\uE7C3"
    };
}
