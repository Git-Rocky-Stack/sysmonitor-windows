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
    private readonly IDuplicateFinder _duplicateFinder;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _scanCts;
    private bool _isDisposed;

    public ObservableCollection<DuplicateGroupDisplay> DuplicateGroups { get; } = [];

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

    public DuplicateFinderViewModel(IDuplicateFinder duplicateFinder)
    {
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
        catch { }
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

        var freedBytes = await _duplicateFinder.DeleteDuplicatesAsync(filesToDelete);

        _dispatcherQueue.TryEnqueue(() =>
        {
            // Remove deleted files from the display
            foreach (var group in DuplicateGroups.ToList())
            {
                var filesToRemove = group.Files.Where(f => f.IsSelected && !f.IsOriginal).ToList();
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

            ShowAction($"Deleted {filesToDelete.Count} files, freed {FormatSize(freedBytes)}", true);
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
