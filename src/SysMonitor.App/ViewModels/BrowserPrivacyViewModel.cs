using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Services.Cleaners;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class BrowserPrivacyViewModel : ObservableObject
{
    private readonly IBrowserPrivacyCleaner _browserPrivacyCleaner;

    [ObservableProperty] private ObservableCollection<BrowserGroupViewModel> _browserGroups = new();
    [ObservableProperty] private ObservableCollection<InstalledBrowser> _installedBrowsers = new();
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isCleaning;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _statusMessage = "Click 'Scan' to find browser privacy data";
    [ObservableProperty] private long _totalSizeBytes;
    [ObservableProperty] private long _selectedSizeBytes;
    [ObservableProperty] private string _formattedTotalSize = "0 MB";
    [ObservableProperty] private string _formattedSelectedSize = "0 MB";
    [ObservableProperty] private int _totalItems;
    [ObservableProperty] private int _selectedItems;

    // Risk filter
    [ObservableProperty] private bool _showSafeItems = true;
    [ObservableProperty] private bool _showLowRiskItems = true;
    [ObservableProperty] private bool _showMediumRiskItems = true;
    [ObservableProperty] private bool _showHighRiskItems = false;

    private List<BrowserPrivacyItem> _allItems = new();

    public BrowserPrivacyViewModel(IBrowserPrivacyCleaner browserPrivacyCleaner)
    {
        _browserPrivacyCleaner = browserPrivacyCleaner;
    }

    public async Task InitializeAsync()
    {
        var browsers = await _browserPrivacyCleaner.GetInstalledBrowsersAsync();
        InstalledBrowsers = new ObservableCollection<InstalledBrowser>(browsers);
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        StatusMessage = "Scanning browser data...";
        BrowserGroups.Clear();
        _allItems.Clear();

        try
        {
            _allItems = await _browserPrivacyCleaner.ScanAsync();
            ApplyFilter();

            HasResults = _allItems.Count > 0;
            StatusMessage = HasResults
                ? $"Found {TotalItems} items ({FormattedTotalSize}) across {InstalledBrowsers.Count} browsers"
                : "No browser data found";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error scanning: {ex.Message}";
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

        var selectedItems = _allItems.Where(i => i.IsSelected).ToList();
        if (!selectedItems.Any())
        {
            StatusMessage = "No items selected to clean";
            return;
        }

        // Warn about high-risk items
        var highRiskSelected = selectedItems.Where(i => i.RiskLevel == PrivacyRiskLevel.High).ToList();
        if (highRiskSelected.Any())
        {
            // In a real app, we'd show a confirmation dialog here
            // For now, we'll proceed but note it in the status
        }

        IsCleaning = true;
        StatusMessage = "Cleaning selected items...";

        try
        {
            var result = await _browserPrivacyCleaner.CleanAsync(selectedItems);

            if (result.Success)
            {
                StatusMessage = $"Cleaned {FormatSize(result.BytesCleaned)} ({result.ItemsDeleted} items) in {result.Duration.TotalSeconds:F1}s";
                if (result.ErrorCount > 0)
                {
                    StatusMessage += $" - {result.ErrorCount} items skipped (in use)";
                }
            }
            else
            {
                StatusMessage = $"Cleaning completed with errors: {string.Join(", ", result.Errors.Take(3))}";
            }

            await ScanAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error cleaning: {ex.Message}";
        }
        finally
        {
            IsCleaning = false;
        }
    }

    [RelayCommand]
    private void SelectSafeItems()
    {
        foreach (var item in _allItems.Where(i => i.RiskLevel == PrivacyRiskLevel.Safe))
        {
            item.IsSelected = true;
        }
        UpdateSelection();
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var item in _allItems)
        {
            item.IsSelected = false;
        }
        UpdateSelection();
    }

    [RelayCommand]
    private void SelectByBrowser(string browserName)
    {
        foreach (var item in _allItems.Where(i => i.BrowserName == browserName))
        {
            item.IsSelected = true;
        }
        UpdateSelection();
    }

    partial void OnShowSafeItemsChanged(bool value) => ApplyFilter();
    partial void OnShowLowRiskItemsChanged(bool value) => ApplyFilter();
    partial void OnShowMediumRiskItemsChanged(bool value) => ApplyFilter();
    partial void OnShowHighRiskItemsChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filtered = _allItems.Where(item =>
            (ShowSafeItems && item.RiskLevel == PrivacyRiskLevel.Safe) ||
            (ShowLowRiskItems && item.RiskLevel == PrivacyRiskLevel.Low) ||
            (ShowMediumRiskItems && item.RiskLevel == PrivacyRiskLevel.Medium) ||
            (ShowHighRiskItems && item.RiskLevel == PrivacyRiskLevel.High)
        ).ToList();

        // Group by browser
        var groups = filtered.GroupBy(i => i.BrowserName)
                            .Select(g => new BrowserGroupViewModel(g.Key, g.ToList()))
                            .ToList();

        BrowserGroups.Clear();
        foreach (var group in groups)
        {
            BrowserGroups.Add(group);
        }

        UpdateTotals();
    }

    private void UpdateTotals()
    {
        var visibleItems = BrowserGroups.SelectMany(g => g.Items).ToList();
        TotalSizeBytes = visibleItems.Sum(i => i.SizeBytes);
        TotalItems = visibleItems.Count;
        FormattedTotalSize = FormatSize(TotalSizeBytes);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        var selected = _allItems.Where(i => i.IsSelected).ToList();
        SelectedSizeBytes = selected.Sum(i => i.SizeBytes);
        SelectedItems = selected.Count;
        FormattedSelectedSize = FormatSize(SelectedSizeBytes);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}

public partial class BrowserGroupViewModel : ObservableObject
{
    public string BrowserName { get; }
    public string BrowserIcon { get; }
    public ObservableCollection<BrowserPrivacyItem> Items { get; }
    public long TotalSize { get; }
    public string FormattedSize { get; }

    [ObservableProperty] private bool _isExpanded = true;

    public BrowserGroupViewModel(string browserName, List<BrowserPrivacyItem> items)
    {
        BrowserName = browserName;
        Items = new ObservableCollection<BrowserPrivacyItem>(items);
        TotalSize = items.Sum(i => i.SizeBytes);
        FormattedSize = FormatSize(TotalSize);
        BrowserIcon = items.FirstOrDefault()?.BrowserIcon ?? "\uE774";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}
