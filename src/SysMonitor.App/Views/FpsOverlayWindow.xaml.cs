using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SysMonitor.Core.Services.GameMode;
using Windows.Foundation;
using Windows.Graphics;
using WinRT.Interop;

namespace SysMonitor.App.Views;

/// <summary>
/// F1 Telemetry-style overlay window with LED bars and drag-to-reposition.
/// </summary>
public sealed partial class FpsOverlayWindow : Window
{
    // Win32 constants - not click-through so user can drag
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    private readonly IntPtr _hwnd;
    private readonly AppWindow _appWindow;

    // Drag state
    private bool _isDragging;
    private Point _dragStartPoint;
    private int _windowStartX;
    private int _windowStartY;

    // LED references
    private Border[] _cpuLeds = null!;
    private Border[] _gpuLeds = null!;
    private Border[] _ramLeds = null!;
    private Border[] _pwrLeds = null!;
    private Border[] _fanDots = null!;

    public FpsOverlayWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        // Remove title bar and borders
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.SetBorderAndTitleBar(false, false);
        }

        // Set window as tool window (no taskbar) but NOT click-through
        SetWindowStyles();

        // Set size and initial position (taller for power/fan rows)
        _appWindow.MoveAndResize(new RectInt32(100, 100, 220, 260));

        // Initialize LED arrays
        InitializeLedArrays();
    }

    private void SetWindowStyles()
    {
        int exStyle = GetWindowLong(_hwnd, GWL_EXSTYLE);
        exStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
        SetWindowLong(_hwnd, GWL_EXSTYLE, exStyle);
    }

    private void InitializeLedArrays()
    {
        _cpuLeds = new Border[] { CpuLed0, CpuLed1, CpuLed2, CpuLed3, CpuLed4, CpuLed5, CpuLed6, CpuLed7, CpuLed8, CpuLed9 };
        _gpuLeds = new Border[] { GpuLed0, GpuLed1, GpuLed2, GpuLed3, GpuLed4, GpuLed5, GpuLed6, GpuLed7, GpuLed8, GpuLed9 };
        _ramLeds = new Border[] { RamLed0, RamLed1, RamLed2, RamLed3, RamLed4, RamLed5, RamLed6, RamLed7, RamLed8, RamLed9 };
        _pwrLeds = new Border[] { PwrLed0, PwrLed1, PwrLed2, PwrLed3, PwrLed4, PwrLed5, PwrLed6, PwrLed7, PwrLed8, PwrLed9 };
        _fanDots = new Border[] { FanDot0, FanDot1, FanDot2, FanDot3, FanDot4 };
    }

    public OverlayPosition Position { get; set; } = OverlayPosition.TopRight;

    #region Drag Handling

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(RootGrid);
        if (pointer.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            _dragStartPoint = pointer.Position;
            _windowStartX = _appWindow.Position.X;
            _windowStartY = _appWindow.Position.Y;
            RootGrid.CapturePointer(e.Pointer);
            e.Handled = true;
        }
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            var pointer = e.GetCurrentPoint(RootGrid);
            var deltaX = (int)(pointer.Position.X - _dragStartPoint.X);
            var deltaY = (int)(pointer.Position.Y - _dragStartPoint.Y);

            _appWindow.Move(new PointInt32(_windowStartX + deltaX, _windowStartY + deltaY));
            e.Handled = true;
        }
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            RootGrid.ReleasePointerCapture(e.Pointer);
            e.Handled = true;
        }
    }

    #endregion

    public void UpdateStats(OverlayStats stats)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            // Update FPS display
            FpsText.Text = stats.Fps > 0 ? stats.Fps.ToString() : "---";

            // Update status LED color based on overall health
            UpdateStatusLed(stats);

            // Update CPU (convert Celsius to Fahrenheit)
            var cpuTempF = (stats.CpuTemperature * 1.8) + 32;
            CpuTempText.Text = stats.CpuTemperature > 0 ? $"{cpuTempF:F0}" : "--";
            UpdateLedBar(_cpuLeds, stats.CpuUsage);

            // Update GPU (convert Celsius to Fahrenheit)
            var gpuTempF = (stats.GpuTemperature * 1.8) + 32;
            GpuTempText.Text = stats.GpuTemperature > 0 ? $"{gpuTempF:F0}" : "--";
            UpdateLedBar(_gpuLeds, stats.GpuUsage);

            // Update RAM
            var ramPercent = stats.RamTotalGb > 0 ? (stats.RamUsageGb / stats.RamTotalGb) * 100 : 0;
            RamText.Text = $"{stats.RamUsageGb:F0}/{stats.RamTotalGb:F0}GB";
            UpdateLedBar(_ramLeds, ramPercent);

            // Update Power (scale: 0-300W = 0-100%)
            PowerText.Text = stats.SystemPowerWatts > 0 ? $"{stats.SystemPowerWatts:F0}" : "--";
            var powerPercent = Math.Min(100, (stats.SystemPowerWatts / 300.0) * 100);
            UpdateLedBar(_pwrLeds, powerPercent);

            // Update Fan Speed
            var maxFanSpeed = stats.FanSpeeds.Count > 0 ? stats.FanSpeeds.Values.Max() : 0;
            FanText.Text = maxFanSpeed > 0 ? $"{maxFanSpeed:F0}" : "----";
            UpdateFanDots(maxFanSpeed);
        });
    }

    private void UpdateFanDots(double rpm)
    {
        // Fan dots light up based on RPM ranges (0-500, 500-1000, 1000-1500, 1500-2000, 2000+)
        int litCount = rpm switch
        {
            >= 2000 => 5,
            >= 1500 => 4,
            >= 1000 => 3,
            >= 500 => 2,
            > 0 => 1,
            _ => 0
        };

        for (int i = 0; i < _fanDots.Length; i++)
        {
            _fanDots[i].Opacity = i < litCount ? 1.0 : 0.3;
        }
    }

    private void UpdateStatusLed(OverlayStats stats)
    {
        // Determine overall status color based on temps and usage
        var maxTemp = Math.Max(stats.CpuTemperature, stats.GpuTemperature);
        var maxUsage = Math.Max(stats.CpuUsage, stats.GpuUsage);

        Windows.UI.Color ledColor;
        if (maxTemp >= 90 || maxUsage >= 95)
        {
            ledColor = Windows.UI.Color.FromArgb(255, 255, 0, 0); // Red - Critical
        }
        else if (maxTemp >= 80 || maxUsage >= 85)
        {
            ledColor = Windows.UI.Color.FromArgb(255, 255, 102, 0); // Orange - Warning
        }
        else if (maxTemp >= 70 || maxUsage >= 70)
        {
            ledColor = Windows.UI.Color.FromArgb(255, 255, 204, 0); // Yellow - Moderate
        }
        else
        {
            ledColor = Windows.UI.Color.FromArgb(255, 0, 255, 0); // Green - Good
        }

        StatusLed.Fill = new SolidColorBrush(ledColor);
        StatusLedGlow.Fill = new SolidColorBrush(ledColor);
    }

    private void UpdateLedBar(Border[] leds, double percentage)
    {
        // Calculate how many LEDs should be lit (0-10)
        int litCount = (int)Math.Ceiling(percentage / 10.0);
        litCount = Math.Clamp(litCount, 0, 10);

        for (int i = 0; i < leds.Length; i++)
        {
            // LED is lit if index < litCount
            leds[i].Opacity = i < litCount ? 1.0 : 0.2;
        }
    }

    public void ShowOverlay()
    {
        Activate();
    }

    public void HideOverlay()
    {
        _appWindow.Hide();
    }
}
