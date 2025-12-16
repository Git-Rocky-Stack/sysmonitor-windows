using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Cleaners;
using System.Collections.ObjectModel;
using System;

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

        var selectedCount = ScanResults.Count(r => r.IsSelected);
        if (selectedCount == 0)
        {
            StatusMessage = "No issues selected. Select issues to fix first.";
            return;
        }

        IsCleaning = true;

        try
        {
            // Create backup first
            StatusMessage = "Creating registry backup...";
            LastBackupPath = await _registryCleaner.BackupRegistryAsync();

            var selected = ScanResults.Where(r => r.IsSelected).ToList();

            // Check if any issues require elevation (HKLM/HKCR keys)
            var requiresElevation = ElevatedRegistryHelper.RequiresElevation(selected);
            var isAlreadyElevated = ElevatedRegistryHelper.IsRunningElevated();

            int totalFixed = 0;
            int totalErrors = 0;

            if (requiresElevation && !isAlreadyElevated)
            {
                // Separate issues into elevated (HKLM/HKCR) and non-elevated (HKCU)
                var elevatedIssues = selected.Where(i =>
                    i.Key.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase) ||
                    i.Key.StartsWith("HKLM", StringComparison.OrdinalIgnoreCase) ||
                    i.Key.StartsWith("HKEY_CLASSES_ROOT", StringComparison.OrdinalIgnoreCase) ||
                    i.Key.StartsWith("HKCR", StringComparison.OrdinalIgnoreCase)).ToList();

                var nonElevatedIssues = selected.Where(i =>
                    i.Key.StartsWith("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) ||
                    i.Key.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase)).ToList();

                // First, clean non-elevated issues normally
                if (nonElevatedIssues.Count > 0)
                {
                    StatusMessage = $"Fixing {nonElevatedIssues.Count:N0} user registry issues...";
                    var normalResult = await _registryCleaner.CleanAsync(nonElevatedIssues);
                    totalFixed += normalResult.FilesDeleted;
                    totalErrors += normalResult.ErrorCount;
                }

                // Then, clean elevated issues with UAC prompt
                if (elevatedIssues.Count > 0)
                {
                    StatusMessage = $"Requesting admin rights for {elevatedIssues.Count:N0} system registry issues...";

                    var elevatedResult = await ElevatedRegistryHelper.RunElevatedCleanAsync(elevatedIssues);

                    if (elevatedResult.WasCancelled)
                    {
                        StatusMessage = totalFixed > 0
                            ? $"Fixed {totalFixed:N0} user issues. Elevation cancelled - {elevatedIssues.Count:N0} system issues skipped."
                            : "Elevation cancelled by user. No system registry issues were fixed.";
                        await ScanAsync();
                        return;
                    }

                    if (elevatedResult.Success)
                    {
                        totalFixed += elevatedResult.FixedCount;
                        totalErrors += elevatedResult.ErrorCount;
                    }
                    else
                    {
                        totalErrors += elevatedIssues.Count;
                    }
                }
            }
            else
            {
                // Either no elevation needed, or already running as admin
                StatusMessage = $"Fixing {selectedCount:N0} registry issues...";
                var result = await _registryCleaner.CleanAsync(selected);
                totalFixed = result.FilesDeleted;
                totalErrors = result.ErrorCount;
            }

            FixedCount = totalFixed;

            // Show comprehensive status
            if (totalFixed == 0 && totalErrors > 0)
            {
                StatusMessage = $"Could not fix issues. {totalErrors:N0} errors occurred.";
            }
            else if (totalErrors > 0)
            {
                StatusMessage = $"Fixed {totalFixed:N0} of {selectedCount:N0} issues. {totalErrors:N0} could not be fixed.";
            }
            else if (totalFixed > 0)
            {
                StatusMessage = $"Successfully fixed {totalFixed:N0} registry issues!";
            }
            else
            {
                StatusMessage = "No changes were needed - issues may have been resolved already.";
            }

            // Rescan to update the list
            await ScanAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error during cleanup: {ex.Message}";
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
