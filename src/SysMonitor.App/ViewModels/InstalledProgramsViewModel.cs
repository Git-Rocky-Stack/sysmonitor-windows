using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class InstalledProgramsViewModel : ObservableObject
{
    private readonly IInstalledProgramsService _programsService;

    [ObservableProperty] private ObservableCollection<InstalledProgram> _programs = new();
    [ObservableProperty] private ObservableCollection<InstalledProgram> _filteredPrograms = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isUninstalling;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _statusMessage = "Click 'Refresh' to load installed programs";
    [ObservableProperty] private int _totalCount;
    [ObservableProperty] private int _filteredCount;
    [ObservableProperty] private string _totalSize = "0 MB";
    [ObservableProperty] private InstalledProgram? _selectedProgram;

    // Filter toggles
    [ObservableProperty] private bool _showWin32Apps = true;
    [ObservableProperty] private bool _showStoreApps = true;
    [ObservableProperty] private bool _showSystemApps = true;
    [ObservableProperty] private bool _showFrameworks = false;

    public InstalledProgramsViewModel(IInstalledProgramsService programsService)
    {
        _programsService = programsService;
    }

    [RelayCommand]
    private async Task LoadProgramsAsync()
    {
        IsLoading = true;
        StatusMessage = "Loading installed programs...";

        try
        {
            var programs = await _programsService.GetInstalledProgramsAsync();

            Programs.Clear();
            foreach (var program in programs)
            {
                Programs.Add(program);
            }

            TotalCount = Programs.Count;
            TotalSize = FormatSize(Programs.Sum(p => p.EstimatedSizeBytes));

            ApplyFilters();

            var storeCount = Programs.Count(p => p.Type == ProgramType.StoreApp);
            var systemCount = Programs.Count(p => p.Type == ProgramType.SystemApp);
            var win32Count = Programs.Count(p => p.Type == ProgramType.Win32);

            StatusMessage = $"Found {TotalCount} programs ({win32Count} Desktop, {storeCount} Store, {systemCount} System)";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task UninstallProgramAsync(InstalledProgram? program)
    {
        if (program == null || !program.CanUninstall) return;

        IsUninstalling = true;
        StatusMessage = $"Uninstalling {program.Name}...";

        try
        {
            var result = await _programsService.UninstallProgramAsync(program);

            if (result.Success)
            {
                StatusMessage = $"Uninstalled {program.Name} successfully";
                // Refresh the list after a short delay
                await Task.Delay(2000);
                await LoadProgramsAsync();
            }
            else
            {
                StatusMessage = $"Uninstall failed: {result.Message}";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsUninstalling = false;
        }
    }

    [RelayCommand]
    private void OpenInstallLocation(InstalledProgram? program)
    {
        if (program == null) return;
        _programsService.OpenInstallLocation(program);
    }

    partial void OnSearchTextChanged(string value)
    {
        ApplyFilters();
    }

    partial void OnShowWin32AppsChanged(bool value) => ApplyFilters();
    partial void OnShowStoreAppsChanged(bool value) => ApplyFilters();
    partial void OnShowSystemAppsChanged(bool value) => ApplyFilters();
    partial void OnShowFrameworksChanged(bool value) => ApplyFilters();

    private void ApplyFilters()
    {
        var filtered = Programs.AsEnumerable();

        // Apply type filters
        filtered = filtered.Where(p =>
            (ShowWin32Apps && p.Type == ProgramType.Win32) ||
            (ShowStoreApps && p.Type == ProgramType.StoreApp) ||
            (ShowSystemApps && p.Type == ProgramType.SystemApp) ||
            (ShowFrameworks && p.Type == ProgramType.Framework));

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            filtered = filtered.Where(p =>
                p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                p.Publisher.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        FilteredPrograms.Clear();
        foreach (var program in filtered)
        {
            FilteredPrograms.Add(program);
        }

        FilteredCount = FilteredPrograms.Count;
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
