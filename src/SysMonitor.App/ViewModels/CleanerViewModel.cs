using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Cleaners;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class CleanerViewModel : ObservableObject
{
    private readonly ITempFileCleaner _tempFileCleaner;
    private readonly IBrowserCacheCleaner _browserCacheCleaner;

    [ObservableProperty] private ObservableCollection<CleanerScanResult> _scanResults = new();
    [ObservableProperty] private bool _isScanning = false;
    [ObservableProperty] private bool _isCleaning = false;
    [ObservableProperty] private bool _hasResults = false;
    [ObservableProperty] private double _totalSizeMB = 0;
    [ObservableProperty] private double _selectedSizeMB = 0;
    [ObservableProperty] private int _totalFiles = 0;
    [ObservableProperty] private string _statusMessage = "Click 'Scan' to find cleanable files";
    [ObservableProperty] private double _cleanedMB = 0;
    [ObservableProperty] private string _formattedTotalSize = "0 MB";

    public CleanerViewModel(ITempFileCleaner tempFileCleaner, IBrowserCacheCleaner browserCacheCleaner)
    {
        _tempFileCleaner = tempFileCleaner;
        _browserCacheCleaner = browserCacheCleaner;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        StatusMessage = "Scanning...";
        ScanResults.Clear();

        try
        {
            var tempResults = await _tempFileCleaner.ScanAsync();
            var browserResults = await _browserCacheCleaner.ScanAsync();

            foreach (var r in tempResults) ScanResults.Add(r);
            foreach (var r in browserResults) ScanResults.Add(r);

            var totalBytes = ScanResults.Sum(r => r.SizeBytes);
            TotalSizeMB = ScanResults.Sum(r => r.SizeMB);
            SelectedSizeMB = ScanResults.Where(r => r.IsSelected).Sum(r => r.SizeMB);
            TotalFiles = ScanResults.Sum(r => r.FileCount);
            HasResults = ScanResults.Count > 0;
            FormattedTotalSize = FormatSize(totalBytes);
            StatusMessage = $"Found {TotalFiles:N0} files ({FormattedTotalSize}) that can be cleaned";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task CleanAsync()
    {
        if (!HasResults) return;
        IsCleaning = true;
        StatusMessage = "Cleaning...";

        try
        {
            var selected = ScanResults.Where(r => r.IsSelected).ToList();
            var tempItems = selected.Where(r => r.Category != CleanerCategory.BrowserCache);
            var browserItems = selected.Where(r => r.Category == CleanerCategory.BrowserCache);

            var result1 = await _tempFileCleaner.CleanAsync(tempItems);
            var result2 = await _browserCacheCleaner.CleanAsync(browserItems);

            CleanedMB = result1.MBCleaned + result2.MBCleaned;
            StatusMessage = $"Cleaned {CleanedMB:F1} MB successfully!";

            await ScanAsync();
        }
        finally
        {
            IsCleaning = false;
        }
    }

    [RelayCommand]
    private void ToggleSelectAll()
    {
        bool newState = !ScanResults.All(r => r.IsSelected);
        foreach (var r in ScanResults) r.IsSelected = newState;
        UpdateSelectedSize();
    }

    private void UpdateSelectedSize()
    {
        SelectedSizeMB = ScanResults.Where(r => r.IsSelected).Sum(r => r.SizeMB);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824)
            return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}
