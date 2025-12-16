using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;
using Windows.Storage.Pickers;

namespace SysMonitor.App.ViewModels;

public partial class DriverUpdaterViewModel : ObservableObject, IDisposable
{
    private readonly IDriverUpdater _driverUpdater;
    private readonly DispatcherQueue _dispatcherQueue;
    private CancellationTokenSource? _scanCts;
    private bool _isDisposed;

    public ObservableCollection<DriverDisplayItem> Drivers { get; } = [];
    public ObservableCollection<DriverDisplayItem> ProblemDrivers { get; } = [];

    // Stats
    [ObservableProperty] private int _totalDrivers;
    [ObservableProperty] private int _upToDateDrivers;
    [ObservableProperty] private int _outdatedDrivers;
    [ObservableProperty] private int _problemDriversCount;
    [ObservableProperty] private int _unsignedDrivers;

    // State
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _scanStatus = "Ready to scan";
    [ObservableProperty] private string _lastScanTime = "Never";
    [ObservableProperty] private bool _hasScanned;

    // Filter
    [ObservableProperty] private string _filterText = "";
    [ObservableProperty] private bool _showProblemsOnly;

    public DriverUpdaterViewModel(IDriverUpdater driverUpdater)
    {
        _driverUpdater = driverUpdater;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    public async Task InitializeAsync()
    {
        await ScanDriversAsync();
    }

    [RelayCommand]
    private async Task ScanDriversAsync()
    {
        if (IsScanning)
            return;

        _scanCts = new CancellationTokenSource();
        IsScanning = true;
        ScanStatus = "Scanning drivers...";
        Drivers.Clear();
        ProblemDrivers.Clear();

        try
        {
            var drivers = await _driverUpdater.ScanDriversAsync(_scanCts.Token);

            _dispatcherQueue.TryEnqueue(() =>
            {
                foreach (var driver in drivers)
                {
                    var displayItem = new DriverDisplayItem(driver);
                    Drivers.Add(displayItem);

                    if (driver.HasProblem || driver.IsOutdated || !driver.IsSigned)
                    {
                        ProblemDrivers.Add(displayItem);
                    }
                }

                TotalDrivers = Drivers.Count;
                ProblemDriversCount = Drivers.Count(d => d.HasProblem);
                OutdatedDrivers = Drivers.Count(d => d.IsOutdated);
                UnsignedDrivers = Drivers.Count(d => !d.IsSigned);
                UpToDateDrivers = Drivers.Count(d => !d.HasProblem && !d.IsOutdated && d.IsSigned);

                HasScanned = true;
                LastScanTime = DateTime.Now.ToString("MMM dd, yyyy HH:mm");
                ScanStatus = $"Scan complete - {TotalDrivers} drivers found";
            });
        }
        catch (OperationCanceledException)
        {
            _dispatcherQueue.TryEnqueue(() => ScanStatus = "Scan cancelled");
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() => ScanStatus = $"Scan failed: {ex.Message}");
        }
        finally
        {
            _dispatcherQueue.TryEnqueue(() => IsScanning = false);
        }
    }

    [RelayCommand]
    private void StopScan()
    {
        _scanCts?.Cancel();
    }

    [RelayCommand]
    private void OpenDeviceManager()
    {
        _driverUpdater.OpenDeviceManager();
    }

    [RelayCommand]
    private void OpenWindowsUpdate()
    {
        _driverUpdater.OpenWindowsUpdate();
    }

    [RelayCommand]
    private async Task ExportReportAsync()
    {
        try
        {
            var savePicker = new FileSavePicker();
            savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            savePicker.FileTypeChoices.Add("Text File", new List<string> { ".txt" });
            savePicker.SuggestedFileName = $"DriverReport_{DateTime.Now:yyyyMMdd_HHmmss}";

            // Get the window handle for the picker
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

            var file = await savePicker.PickSaveFileAsync();
            if (file != null)
            {
                ScanStatus = "Exporting report...";
                await _driverUpdater.ExportDriverReportAsync(file.Path);
                ScanStatus = $"Report saved to {file.Name}";
            }
        }
        catch (Exception ex)
        {
            ScanStatus = $"Export failed: {ex.Message}";
        }
    }

    partial void OnFilterTextChanged(string value)
    {
        ApplyFilter();
    }

    partial void OnShowProblemsOnlyChanged(bool value)
    {
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        // Filter is applied via the view's CollectionViewSource
        // This method can be used for additional filtering logic if needed
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _scanCts?.Cancel();
        _scanCts?.Dispose();
    }
}

public partial class DriverDisplayItem : ObservableObject
{
    public string DeviceName { get; }
    public string DeviceId { get; }
    public string Manufacturer { get; }
    public string DriverVersion { get; }
    public string DriverDateText { get; }
    public string DriverProvider { get; }
    public string DeviceClass { get; }
    public string DeviceClassIcon { get; }
    public bool IsSigned { get; }
    public string SignedText { get; }
    public string SignedColor { get; }
    public string Status { get; }
    public string StatusColor { get; }
    public bool HasProblem { get; }
    public string ProblemDescription { get; }
    public int DaysSinceUpdate { get; }
    public string AgeText { get; }
    public bool IsOutdated { get; }
    public bool IsCritical { get; }
    public string CriticalBadgeVisibility { get; }
    public string OutdatedBadgeVisibility { get; }
    public string ProblemBadgeVisibility { get; }

    public DriverDisplayItem(DriverInfo info)
    {
        DeviceName = info.DeviceName;
        DeviceId = info.DeviceId;
        Manufacturer = string.IsNullOrEmpty(info.Manufacturer) ? "Unknown" : info.Manufacturer;
        DriverVersion = string.IsNullOrEmpty(info.DriverVersion) ? "Unknown" : info.DriverVersion;
        DriverDateText = info.DriverDate?.ToString("MMM dd, yyyy") ?? "Unknown";
        DriverProvider = string.IsNullOrEmpty(info.DriverProvider) ? "Unknown" : info.DriverProvider;
        DeviceClass = string.IsNullOrEmpty(info.DeviceClass) ? "Other" : info.DeviceClass;
        DeviceClassIcon = info.DeviceClassIcon;
        IsSigned = info.IsSigned;
        SignedText = info.IsSigned ? "Signed" : "Unsigned";
        SignedColor = info.IsSigned ? "#4CAF50" : "#FF9800";
        Status = info.Status;
        StatusColor = info.StatusColor;
        HasProblem = info.HasProblem;
        ProblemDescription = info.ProblemDescription;
        DaysSinceUpdate = info.DaysSinceUpdate;
        IsOutdated = info.IsOutdated;
        IsCritical = info.IsCritical;

        // Age text
        if (info.DaysSinceUpdate <= 0)
            AgeText = "Unknown age";
        else if (info.DaysSinceUpdate < 30)
            AgeText = "< 1 month old";
        else if (info.DaysSinceUpdate < 365)
            AgeText = $"{info.DaysSinceUpdate / 30} months old";
        else
            AgeText = $"{info.DaysSinceUpdate / 365} years old";

        // Badge visibility (for XAML binding - returns "Visible" or "Collapsed")
        CriticalBadgeVisibility = info.IsCritical ? "Visible" : "Collapsed";
        OutdatedBadgeVisibility = info.IsOutdated ? "Visible" : "Collapsed";
        ProblemBadgeVisibility = info.HasProblem ? "Visible" : "Collapsed";
    }
}
