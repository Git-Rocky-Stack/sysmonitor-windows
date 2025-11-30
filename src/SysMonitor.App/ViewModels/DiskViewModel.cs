using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Monitors;
using System.Collections.ObjectModel;

namespace SysMonitor.App.ViewModels;

public partial class DiskViewModel : ObservableObject, IDisposable
{
    private readonly IDiskMonitor _diskMonitor;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _cts;
    private bool _isDisposed;
    private bool _isInitialized;

    // Disk Collection
    public ObservableCollection<DiskDisplayInfo> Disks { get; } = [];

    // Totals
    [ObservableProperty] private double _totalStorageGB;
    [ObservableProperty] private double _totalUsedGB;
    [ObservableProperty] private double _totalFreeGB;
    [ObservableProperty] private double _totalUsagePercent;

    // Total Storage Status
    [ObservableProperty] private string _totalStorageStatus = "Checking...";
    [ObservableProperty] private string _totalStorageColor = "#4CAF50";

    // State
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private bool _hasDisks;

    public DiskViewModel(IDiskMonitor diskMonitor)
    {
        _diskMonitor = diskMonitor;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        await RefreshDataAsync();
        StartAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _cts = new CancellationTokenSource();
        _ = RefreshLoopAsync(_cts.Token);
    }

    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10)); // Disk changes slowly
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshDataAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when disposed
        }
    }

    private async Task RefreshDataAsync()
    {
        if (_isDisposed) return;

        try
        {
            var disks = await _diskMonitor.GetAllDisksAsync();
            if (_isDisposed) return;

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (_isDisposed) return;

                Disks.Clear();

                double totalStorage = 0;
                double totalUsed = 0;
                double totalFree = 0;

                foreach (var disk in disks)
                {
                    totalStorage += disk.TotalGB;
                    totalUsed += disk.UsedGB;
                    totalFree += disk.FreeGB;

                    Disks.Add(new DiskDisplayInfo
                    {
                        Name = disk.Name,
                        Label = string.IsNullOrEmpty(disk.Label) ? "Local Disk" : disk.Label,
                        DriveType = disk.DriveType,
                        FileSystem = disk.FileSystem,
                        TotalGB = disk.TotalGB,
                        UsedGB = disk.UsedGB,
                        FreeGB = disk.FreeGB,
                        UsagePercent = disk.UsagePercent,
                        IsSSD = disk.IsSSD,
                        DriveIcon = GetDriveIcon(disk.DriveType, disk.IsSSD),
                        UsageColor = GetUsageColor(disk.UsagePercent),
                        UsageStatus = GetUsageStatus(disk.UsagePercent),
                        StorageType = disk.IsSSD ? "SSD" : "HDD"
                    });
                }

                TotalStorageGB = totalStorage;
                TotalUsedGB = totalUsed;
                TotalFreeGB = totalFree;
                TotalUsagePercent = totalStorage > 0 ? (totalUsed / totalStorage) * 100 : 0;

                // Update total storage status
                (TotalStorageStatus, TotalStorageColor) = GetStorageStatus(TotalUsagePercent);

                HasDisks = Disks.Count > 0;
                IsLoading = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception)
        {
            // Log in production
        }
    }

    private static string GetDriveIcon(string driveType, bool isSSD)
    {
        return driveType switch
        {
            "Fixed" => isSSD ? "\uE8B7" : "\uEDA2", // SSD vs HDD
            "Removable" => "\uE8F1", // USB drive
            "CDRom" => "\uE958", // CD/DVD
            "Network" => "\uE8CE", // Network drive
            _ => "\uEDA2" // Default disk
        };
    }

    private static string GetUsageColor(double usagePercent)
    {
        return usagePercent switch
        {
            >= 90 => "#F44336", // Red - Critical
            >= 75 => "#FF9800", // Orange - Warning
            >= 50 => "#FFC107", // Yellow - Moderate
            _ => "#4CAF50"      // Green - Good
        };
    }

    private static (string status, string color) GetStorageStatus(double usagePercent)
    {
        return usagePercent switch
        {
            >= 90 => ("Critical - Free up space immediately", "#F44336"),
            >= 75 => ("Warning - Consider cleaning up", "#FF9800"),
            >= 50 => ("Moderate - Storage usage normal", "#8BC34A"),
            _ => ("Excellent - Plenty of free space", "#4CAF50")
        };
    }

    private static string GetUsageStatus(double usagePercent)
    {
        return usagePercent switch
        {
            >= 90 => "Critical",
            >= 75 => "Low Space",
            >= 50 => "Normal",
            _ => "Healthy"
        };
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _cts?.Cancel();
        _cts?.Dispose();
    }
}

public class DiskDisplayInfo
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string DriveType { get; set; } = string.Empty;
    public string FileSystem { get; set; } = string.Empty;
    public double TotalGB { get; set; }
    public double UsedGB { get; set; }
    public double FreeGB { get; set; }
    public double UsagePercent { get; set; }
    public bool IsSSD { get; set; }
    public string DriveIcon { get; set; } = string.Empty;
    public string UsageColor { get; set; } = string.Empty;
    public string UsageStatus { get; set; } = string.Empty;
    public string StorageType { get; set; } = string.Empty;

    public string FormattedTotal => $"{TotalGB:F1} GB";
    public string FormattedUsed => $"{UsedGB:F1} GB";
    public string FormattedFree => $"{FreeGB:F1} GB";
    public string FormattedPercent => $"{UsagePercent:F1}%";
}
