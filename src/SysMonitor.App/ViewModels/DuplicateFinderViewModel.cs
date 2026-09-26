using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;

namespace SysMonitor.App.ViewModels;

public partial class DuplicateFinderViewModel : ObservableObject, IDisposable
{
    private readonly ILogger _logger;

    private readonly IDuplicateFinder _duplicateFinder;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _scanCts;

    /// <summary>Counts the messages shown, so a timer only clears the one it was started for.</summary>
    private int _actionShown;

    private bool _isDisposed;

    public ObservableCollection<DuplicateGroupDisplay> DuplicateGroups { get; } = [];

    /// <summary>
    /// Asked before anything is deleted, with how many files and how much space. The page puts it on screen;
    /// nothing is deleted unless it comes back true.
    /// </summary>
    public Func<int, long, Task<bool>>? ConfirmDeletion { get; set; }

    // Scan Settings
    [ObservableProperty] private string _scanPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    // Stats
    [ObservableProperty] private int _groupsFound;
    [ObservableProperty] private int _totalDuplicates;
    [ObservableProperty] private string _wastedSpace = "0 B";
    [ObservableProperty] private long _wastedSpaceBytes;

    // Progress
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private double _scanProgress;
    [ObservableProperty] private string _scanStatus = "Ready to scan";
    [ObservableProperty] private string _currentFile = "";

    // Action Status
    [ObservableProperty] private string _actionStatus = "";
    [ObservableProperty] private bool _hasActionStatus;
    [ObservableProperty] private string _actionStatusColor = "#4CAF50";

    public DuplicateFinderViewModel(IDuplicateFinder duplicateFinder,
        ILogger<DuplicateFinderViewModel>? logger = null)
    {
        _logger = logger ?? NullLogger<DuplicateFinderViewModel>.Instance;
        _duplicateFinder = duplicateFinder;
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
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "BrowseFolderAsync failed");
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
        ScanStatus = "Indexing files...";
        DuplicateGroups.Clear();
        WastedSpaceBytes = 0;
        GroupsFound = 0;
        TotalDuplicates = 0;

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

            var results = await _duplicateFinder.ScanAsync(ScanPath, progress, _scanCts.Token);

            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var group in results)
                {
                    var displayGroup = new DuplicateGroupDisplay(group);
                    DuplicateGroups.Add(displayGroup);
                    WastedSpaceBytes += group.WastedSpace;
                    TotalDuplicates += group.DuplicateCount;
                }

                GroupsFound = DuplicateGroups.Count;
                WastedSpace = FormatSize(WastedSpaceBytes);
                ScanStatus = $"Scan complete - {GroupsFound} duplicate groups found";
                ScanProgress = 100;
                ShowAction($"Found {TotalDuplicates} duplicates wasting {WastedSpace}", true);
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
    private async Task DeleteSelectedDuplicatesAsync()
    {
        var filesToDelete = new List<string>();

        foreach (var group in DuplicateGroups)
        {
            foreach (var file in group.Files.Where(f => f.IsSelected && !f.IsOriginal))
            {
                filesToDelete.Add(file.FullPath);
            }
        }

        if (filesToDelete.Count == 0)
        {
            ShowAction("No duplicates selected for deletion", false);
            return;
        }

        // Deleting files someone did not mean to delete is the worst thing this page can do, so it asks.
        var totalBytes = DuplicateGroups
            .SelectMany(group => group.Files.Where(f => f.IsSelected && !f.IsOriginal).Select(_ => group.FileSizeBytes))
            .Sum();

        if (ConfirmDeletion == null || !await ConfirmDeletion(filesToDelete.Count, totalBytes))
        {
            ShowAction("Nothing was deleted", false);
            return;
        }

        var results = await _duplicateFinder.DeleteDuplicatesAsync(filesToDelete);

        // Only files no longer where they were leave the list. One Windows could not recycle is still there, and
        // it used to vanish from the list as if it had gone.
        var gone = results.Where(r => r.Result.Outcome is RecycleOutcome.Recycled or RecycleOutcome.Missing
                                                        or RecycleOutcome.NotInRecycleBin)
                          .Select(r => r.Path)
                          .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var succeeded = results.All(r => r.Result.Outcome is RecycleOutcome.Recycled or RecycleOutcome.Missing);
        var report = RecycleReport.Describe(results, FormatSize);

        _dispatcherQueue.TryEnqueue(() =>
        {
            foreach (var group in DuplicateGroups.ToList())
            {
                var filesToRemove = group.Files.Where(f => gone.Contains(f.FullPath)).ToList();
                foreach (var file in filesToRemove)
                {
                    group.Files.Remove(file);
                }

                // Remove group if only original remains
                if (group.Files.Count <= 1)
                {
                    DuplicateGroups.Remove(group);
                }
            }

            // Update stats
            GroupsFound = DuplicateGroups.Count;
            TotalDuplicates = DuplicateGroups.Sum(g => g.DuplicateCount);
            WastedSpaceBytes = DuplicateGroups.Sum(g => g.WastedSpaceBytes);
            WastedSpace = FormatSize(WastedSpaceBytes);

            ShowAction(report, succeeded);
        });
    }

    [RelayCommand]
    private void SelectAllDuplicates()
    {
        foreach (var group in DuplicateGroups)
        {
            foreach (var file in group.Files.Where(f => !f.IsOriginal))
            {
                file.IsSelected = true;
            }
        }
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var group in DuplicateGroups)
        {
            foreach (var file in group.Files)
            {
                file.IsSelected = false;
            }
        }
    }

    private void ShowAction(string message, bool isSuccess)
    {
        ActionStatus = message;
        ActionStatusColor = isSuccess ? "#4CAF50" : "#F44336";
        HasActionStatus = true;

        // Good news fades after a few seconds. A report of anything left undone - files Windows could not
        // recycle, a scan that failed - stays until the next message replaces it.
        var shown = ++_actionShown;
        if (isSuccess)
            _ = ClearActionAfterDelayAsync(shown);
    }

    private async Task ClearActionAfterDelayAsync(int shown)
    {
        await Task.Delay(5000);

        // Only the message this timer was started for: a later one is not cleared early by an old timer.
        _dispatcherQueue.TryEnqueue(() =>
        {
            if (shown == _actionShown)
                HasActionStatus = false;
        });
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

public partial class DuplicateGroupDisplay : ObservableObject
{
    public string Hash { get; }
    public long FileSizeBytes { get; }
    public string FormattedSize { get; }
    public long WastedSpaceBytes { get; }
    public string WastedSpace { get; }
    public int DuplicateCount => Files.Count - 1;

    public ObservableCollection<DuplicateFileDisplay> Files { get; } = [];

    [ObservableProperty] private bool _isExpanded = true;

    public DuplicateGroupDisplay(DuplicateGroup group)
    {
        Hash = group.Hash;
        FileSizeBytes = group.FileSize;
        FormattedSize = group.FormattedSize;
        WastedSpaceBytes = group.WastedSpace;
        WastedSpace = FormatSize(group.WastedSpace);

        foreach (var file in group.Files)
        {
            Files.Add(new DuplicateFileDisplay(file));
        }
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
}

public partial class DuplicateFileDisplay : ObservableObject
{
    public string FullPath { get; }
    public string FileName { get; }
    public string Directory { get; }
    public DateTime LastModified { get; }
    public bool IsOriginal { get; }
    public string StatusText { get; }
    public string StatusColor { get; }

    [ObservableProperty] private bool _isSelected;

    public DuplicateFileDisplay(DuplicateFileInfo info)
    {
        FullPath = info.FullPath;
        FileName = info.FileName;
        Directory = info.Directory;
        LastModified = info.LastModified;
        IsOriginal = info.IsOriginal;
        StatusText = info.IsOriginal ? "ORIGINAL" : "DUPLICATE";
        StatusColor = info.IsOriginal ? "#4CAF50" : "#FF9800";
    }
}
