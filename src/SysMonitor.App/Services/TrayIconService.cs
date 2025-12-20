using H.NotifyIcon;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.Core.Services.Alerts;
using SysMonitor.Core.Services.Monitors;
using System.Drawing;
using Windows.UI.Notifications;

namespace SysMonitor.App.Services;

/// <summary>
/// Service for managing the system tray icon with live stats tooltip and context menu.
/// </summary>
public class TrayIconService : IDisposable
{
    private readonly ICpuMonitor _cpuMonitor;
    private readonly IMemoryMonitor _memoryMonitor;
    private readonly IAlertService _alertService;
    private readonly DispatcherQueue _dispatcherQueue;

    private TaskbarIcon? _trayIcon;
    private CancellationTokenSource? _cts;
    private Task? _updateTask;
    private bool _isDisposed;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<string>? NavigateRequested;

    public TrayIconService(
        ICpuMonitor cpuMonitor,
        IMemoryMonitor memoryMonitor,
        IAlertService alertService)
    {
        _cpuMonitor = cpuMonitor;
        _memoryMonitor = memoryMonitor;
        _alertService = alertService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Subscribe to alerts
        _alertService.AlertTriggered += OnAlertTriggered;
    }

    public void Initialize()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "app.ico");

        _trayIcon = new TaskbarIcon();
        _trayIcon.ToolTipText = "SysMonitor - Loading...";

        // Set icon from file
        if (File.Exists(iconPath))
        {
            try
            {
                var icon = new Icon(iconPath);
                _trayIcon.Icon = icon;
            }
            catch
            {
                // Use default icon if loading fails
            }
        }

        // Create context menu
        _trayIcon.ContextMenuMode = H.NotifyIcon.ContextMenuMode.PopupMenu;

        // Set up left click to show window
        _trayIcon.LeftClickCommand = new RelayCommand(() =>
        {
            _dispatcherQueue.TryEnqueue(() => ShowWindowRequested?.Invoke(this, EventArgs.Empty));
        });

        // Build context menu using GeneratedContextMenuPopup
        var menuFlyout = new MenuFlyout();

        var openItem = new MenuFlyoutItem { Text = "Open SysMonitor" };
        openItem.Click += (s, e) => _dispatcherQueue.TryEnqueue(() => ShowWindowRequested?.Invoke(this, EventArgs.Empty));
        menuFlyout.Items.Add(openItem);

        menuFlyout.Items.Add(new MenuFlyoutSeparator());

        var dashboardItem = new MenuFlyoutItem { Text = "Dashboard" };
        dashboardItem.Click += (s, e) => NavigateTo("Dashboard");
        menuFlyout.Items.Add(dashboardItem);

        var cpuItem = new MenuFlyoutItem { Text = "CPU Monitor" };
        cpuItem.Click += (s, e) => NavigateTo("CpuMonitor");
        menuFlyout.Items.Add(cpuItem);

        var memoryItem = new MenuFlyoutItem { Text = "Memory Monitor" };
        memoryItem.Click += (s, e) => NavigateTo("MemoryMonitor");
        menuFlyout.Items.Add(memoryItem);

        var historyItem = new MenuFlyoutItem { Text = "History" };
        historyItem.Click += (s, e) => NavigateTo("History");
        menuFlyout.Items.Add(historyItem);

        menuFlyout.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "Exit" };
        exitItem.Click += (s, e) => _dispatcherQueue.TryEnqueue(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        menuFlyout.Items.Add(exitItem);

        _trayIcon.ContextFlyout = menuFlyout;

        // Force create to show the icon in the tray
        _trayIcon.ForceCreate();

        // Start tooltip update loop
        _cts = new CancellationTokenSource();
        _updateTask = UpdateTooltipLoopAsync(_cts.Token);
    }

    private void NavigateTo(string tag)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            ShowWindowRequested?.Invoke(this, EventArgs.Empty);
            NavigateRequested?.Invoke(this, tag);
        });
    }

    private async Task UpdateTooltipLoopAsync(CancellationToken cancellationToken)
    {
        // Wait a bit before starting
        await Task.Delay(2000, cancellationToken);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var cpuUsage = await _cpuMonitor.GetUsagePercentAsync();
                var memInfo = await _memoryMonitor.GetMemoryInfoAsync();

                var tooltip = $"SysMonitor\nCPU: {cpuUsage:F0}% | RAM: {memInfo.UsagePercent:F0}%";

                _dispatcherQueue.TryEnqueue(() =>
                {
                    if (_trayIcon != null)
                    {
                        _trayIcon.ToolTipText = tooltip;
                    }
                });
            }
            catch
            {
                // Silently continue on errors
            }

            try
            {
                await timer.WaitForNextTickAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void OnAlertTriggered(object? sender, AlertNotification alert)
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            ShowNotification(alert);
        });
    }

    private void ShowNotification(AlertNotification alert)
    {
        try
        {
            // Use Windows toast notification
            var template = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var textNodes = template.GetElementsByTagName("text");

            if (textNodes.Length >= 2)
            {
                textNodes[0].AppendChild(template.CreateTextNode(alert.Title));
                textNodes[1].AppendChild(template.CreateTextNode(alert.Message));
            }

            var notification = new ToastNotification(template);
            ToastNotificationManager.CreateToastNotifier("SysMonitor").Show(notification);
        }
        catch
        {
            // Notifications not available - try balloon tip
            try
            {
                _trayIcon?.ShowNotification(
                    title: alert.Title,
                    message: alert.Message);
            }
            catch { }
        }
    }

    public void ShowIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visibility = Visibility.Visible;
        }
    }

    public void HideIcon()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visibility = Visibility.Collapsed;
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _alertService.AlertTriggered -= OnAlertTriggered;
        _cts?.Cancel();
        _trayIcon?.Dispose();
        _cts?.Dispose();

        GC.SuppressFinalize(this);
    }

    // Simple relay command for tray icon
    private class RelayCommand : System.Windows.Input.ICommand
    {
        private readonly Action _execute;
        public RelayCommand(Action execute) => _execute = execute;
#pragma warning disable CS0067 // Event is never used - required by interface
        public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute();
    }
}
