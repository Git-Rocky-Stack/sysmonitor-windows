using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class HealthCheckViewModel : ObservableObject
{
    private readonly IHealthCheckService _healthCheckService;
    private readonly ISystemRestoreService _systemRestoreService;

    private HealthCheckReport? _currentReport;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _isApplyingFix;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private int _healthScore;
    [ObservableProperty] private string _healthGrade = "?";
    [ObservableProperty] private string _healthDescription = "Run a health check to analyze your system";
    [ObservableProperty] private string _statusMessage = "Click 'Run Health Check' to analyze your system";
    [ObservableProperty] private ObservableCollection<HealthIssueViewModel> _issues = new();
    [ObservableProperty] private bool _canCreateRestorePoint = true;
    [ObservableProperty] private string _lastScanTime = "Never";

    // Summary stats
    [ObservableProperty] private int _criticalCount;
    [ObservableProperty] private int _warningCount;
    [ObservableProperty] private int _infoCount;
    [ObservableProperty] private long _potentialSpaceSavings;
    [ObservableProperty] private string _formattedSpaceSavings = "0 MB";

    public HealthCheckViewModel(IHealthCheckService healthCheckService, ISystemRestoreService systemRestoreService)
    {
        _healthCheckService = healthCheckService;
        _systemRestoreService = systemRestoreService;
    }

    [RelayCommand]
    private async Task RunHealthCheckAsync()
    {
        IsScanning = true;
        StatusMessage = "Analyzing system health...";
        Issues.Clear();

        try
        {
            var progress = new Progress<HealthCheckProgress>(p =>
            {
                StatusMessage = p.CurrentTask;
            });

            _currentReport = await _healthCheckService.RunFullScanAsync(progress);

            foreach (var issue in _currentReport.Issues)
            {
                Issues.Add(new HealthIssueViewModel(issue));
            }

            HealthScore = _currentReport.HealthScore;
            HealthGrade = GetGrade(_currentReport.HealthScore);
            HealthDescription = GetDescription(_currentReport.HealthScore);

            CriticalCount = _currentReport.CriticalIssueCount;
            WarningCount = _currentReport.WarningCount;
            InfoCount = _currentReport.InfoCount;
            PotentialSpaceSavings = _currentReport.TotalJunkBytes + _currentReport.TotalBrowserBytes;
            FormattedSpaceSavings = FormatSize(PotentialSpaceSavings);

            HasResults = true;
            LastScanTime = DateTime.Now.ToString("g");
            StatusMessage = $"Health check complete. Score: {HealthScore}/100 ({HealthGrade})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error during scan: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task QuickFixAsync()
    {
        if (!HasResults || _currentReport == null) return;

        IsApplyingFix = true;
        StatusMessage = "Applying fixes...";

        try
        {
            var progress = new Progress<HealthCheckProgress>(p =>
            {
                StatusMessage = p.CurrentTask;
            });

            var result = await _healthCheckService.ApplyQuickFixAsync(_currentReport, progress);

            if (result.Success)
            {
                StatusMessage = $"Fixed {result.IssuesFixed} issues. Freed {result.FormattedBytesCleaned}.";
                await RunHealthCheckAsync();
            }
            else
            {
                StatusMessage = $"Some fixes failed: {string.Join(", ", result.Errors.Take(3))}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error applying fixes: {ex.Message}";
        }
        finally
        {
            IsApplyingFix = false;
        }
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        CanCreateRestorePoint = false;
        StatusMessage = "Creating system restore point...";

        try
        {
            var result = await _systemRestoreService.CreateRestorePointAsync(
                "SysMonitor - Before Health Fix",
                RestorePointType.ModifySettings);

            if (result.Success)
            {
                StatusMessage = $"Restore point created in {result.Duration.TotalSeconds:F1}s";
            }
            else
            {
                StatusMessage = $"Failed to create restore point: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            CanCreateRestorePoint = true;
        }
    }

    [RelayCommand]
    private void SelectAllFixable()
    {
        foreach (var issue in Issues.Where(i => i.CanAutoFix))
        {
            issue.IsSelected = true;
        }
    }

    [RelayCommand]
    private void DeselectAll()
    {
        foreach (var issue in Issues)
        {
            issue.IsSelected = false;
        }
    }

    private static string GetGrade(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 60 => "D",
        _ => "F"
    };

    private static string GetDescription(int score) => score switch
    {
        >= 90 => "Excellent! Your system is in great shape.",
        >= 80 => "Good. Your system is running well with minor issues.",
        >= 70 => "Fair. Some maintenance is recommended.",
        >= 60 => "Poor. Your system needs attention.",
        _ => "Critical. Immediate maintenance is strongly recommended."
    };

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824) return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576) return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1024) return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}

public partial class HealthIssueViewModel : ObservableObject
{
    public string Title { get; }
    public string Description { get; }
    public string Category { get; }
    public string Severity { get; }
    public string SeverityIcon { get; }
    public bool CanAutoFix { get; }
    public string FixAction { get; }

    [ObservableProperty] private bool _isSelected;

    public HealthIssueViewModel(HealthIssue issue)
    {
        Title = issue.Title;
        Description = issue.Description;
        Category = issue.Category.ToString();
        Severity = issue.Severity.ToString();
        CanAutoFix = issue.CanAutoFix;
        FixAction = issue.FixAction;
        IsSelected = issue.CanAutoFix;

        SeverityIcon = issue.Severity switch
        {
            IssueSeverity.Critical => "\uE783", // Error icon
            IssueSeverity.Warning => "\uE7BA",  // Warning icon
            _ => "\uE946"                        // Info icon
        };
    }
}
