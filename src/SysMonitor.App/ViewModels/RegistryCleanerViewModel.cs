using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Cleaners;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class RegistryCleanerViewModel : ObservableObject
{
    private readonly IRegistryCleaner _registryCleaner;

    [ObservableProperty] private ObservableCollection<RegistryIssue> _scanResults = new();
    [ObservableProperty] private bool _isScanning = false;
    [ObservableProperty] private bool _isCleaning = false;
    [ObservableProperty] private bool _hasResults = false;
    [ObservableProperty] private int _totalIssues = 0;
    [ObservableProperty] private int _selectedIssues = 0;
    [ObservableProperty] private string _statusMessage = "Click 'Scan' to find registry issues";
    [ObservableProperty] private int _fixedCount = 0;
    [ObservableProperty] private string _lastBackupPath = string.Empty;

    public RegistryCleanerViewModel(IRegistryCleaner registryCleaner)
    {
        _registryCleaner = registryCleaner;
    }

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        StatusMessage = "Scanning registry...";
        ScanResults.Clear();

        try
        {
            var results = await _registryCleaner.ScanAsync();

            foreach (var r in results) ScanResults.Add(r);

            TotalIssues = ScanResults.Count;
            SelectedIssues = ScanResults.Count(r => r.IsSelected);
            HasResults = ScanResults.Count > 0;

            StatusMessage = TotalIssues > 0
                ? $"Found {TotalIssues:N0} registry issues that can be fixed"
                : "No registry issues found";
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
        StatusMessage = "Creating backup and fixing registry issues...";

        try
        {
            // Create backup first
            LastBackupPath = await _registryCleaner.BackupRegistryAsync();

            var selected = ScanResults.Where(r => r.IsSelected).ToList();
            var result = await _registryCleaner.CleanAsync(selected);

            FixedCount = result.FilesDeleted; // FilesDeleted is used to count fixed issues

            // Show comprehensive status with error info
            if (result.ErrorCount > 0)
            {
                StatusMessage = $"Fixed {FixedCount:N0} issues. {result.ErrorCount:N0} issues require admin rights.";
            }
            else
            {
                StatusMessage = $"Successfully fixed {FixedCount:N0} registry issues!";
            }

            // Rescan to update the list
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
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void SelectByRisk(string riskLevel)
    {
        var risk = riskLevel switch
        {
            "Safe" => CleanerRiskLevel.Safe,
            "Low" => CleanerRiskLevel.Low,
            "Medium" => CleanerRiskLevel.Medium,
            _ => CleanerRiskLevel.Safe
        };

        foreach (var r in ScanResults)
        {
            r.IsSelected = r.RiskLevel <= risk;
        }
        UpdateSelectedCount();
    }

    private void UpdateSelectedCount()
    {
        SelectedIssues = ScanResults.Count(r => r.IsSelected);
    }

    public string GetCategoryIcon(RegistryIssueCategory category)
    {
        return category switch
        {
            RegistryIssueCategory.InvalidFileReference => "\uE8A5",
            RegistryIssueCategory.OrphanedSoftware => "\uE74C",
            RegistryIssueCategory.InvalidShellExtension => "\uE8B7",
            RegistryIssueCategory.InvalidStartupEntry => "\uE7E8",
            RegistryIssueCategory.ObsoleteMUICache => "\uE74D",
            RegistryIssueCategory.InvalidTypeLib => "\uE8F1",
            RegistryIssueCategory.InvalidCOM => "\uEA86",
            RegistryIssueCategory.InvalidFirewallRule => "\uE83D",
            _ => "\uEA99"
        };
    }

    public string GetRiskColor(CleanerRiskLevel risk)
    {
        return risk switch
        {
            CleanerRiskLevel.Safe => "#4CAF50",
            CleanerRiskLevel.Low => "#8BC34A",
            CleanerRiskLevel.Medium => "#FF9800",
            CleanerRiskLevel.High => "#F44336",
            _ => "#808080"
        };
    }
}
