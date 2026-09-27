using System.Globalization;
using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.Core.Helpers;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// The panel a page shows while the machine works, in place of its content: a faceplate with a live EXEC lamp in
/// its stripe and the busy sweep down a well in its body, and progress only when the work knows how far it has got
/// (System-X src/components/BusyState.tsx).
/// <para>
/// <see cref="Value"/> is the share done, 0 to 100, or not a number while nobody knows: a folder walk does not
/// know how many files it will find until it has found them, and an invented percentage would be a lie. So a bar
/// and a percentage appear only for a finite value. <see cref="CancelCommand"/>, when set, puts a cancel cap in the
/// stripe beside the lamp. The lamp is armed - the app is working on the machine - and strikes when the panel is
/// shown; a screen reader hears it as the title.
/// </para>
/// </summary>
public sealed class BusyPanel : Control
{
    public static readonly DependencyProperty KickerProperty = DependencyProperty.Register(
        nameof(Kicker), typeof(string), typeof(BusyPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(BusyPanel), new PropertyMetadata(string.Empty, OnLookChanged));

    public static readonly DependencyProperty DetailProperty = DependencyProperty.Register(
        nameof(Detail), typeof(string), typeof(BusyPanel), new PropertyMetadata(string.Empty, OnLookChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(BusyPanel), new PropertyMetadata(double.NaN, OnLookChanged));

    public static readonly DependencyProperty ReadoutProperty = DependencyProperty.Register(
        nameof(Readout), typeof(string), typeof(BusyPanel), new PropertyMetadata(string.Empty, OnLookChanged));

    public static readonly DependencyProperty CancelCommandProperty = DependencyProperty.Register(
        nameof(CancelCommand), typeof(ICommand), typeof(BusyPanel), new PropertyMetadata(null, OnLookChanged));

    public static readonly DependencyProperty CancelTextProperty = DependencyProperty.Register(
        nameof(CancelText), typeof(string), typeof(BusyPanel), new PropertyMetadata("Cancel"));

    public static readonly DependencyProperty PercentTextProperty = DependencyProperty.Register(
        nameof(PercentText), typeof(string), typeof(BusyPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SerialTextProperty = DependencyProperty.Register(
        nameof(SerialText), typeof(string), typeof(BusyPanel), new PropertyMetadata(PanelSerial.For(string.Empty)));

    private ScanSweep? _sweep;
    private ProgressBar? _bar;
    private Control? _lamp;

    public BusyPanel()
    {
        IsTabStop = false;
        Loaded += (_, _) => _sweep?.Start();
        Unloaded += (_, _) => _sweep?.Stop();
    }

    /// <summary>The stripe's kicker, conventionally <c>RUN - MODULE - 01A</c>.</summary>
    public string Kicker
    {
        get => (string)GetValue(KickerProperty);
        set => SetValue(KickerProperty, value);
    }

    /// <summary>What the machine is doing, in one line. It also names the panel's serial and its lamp.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>A second line: what is being read, or where.</summary>
    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>The share done, 0 to 100, or not a number when the work does not know its extent.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>A quiet line under the bar, or under the well without one: a count, the current path.</summary>
    public string Readout
    {
        get => (string)GetValue(ReadoutProperty);
        set => SetValue(ReadoutProperty, value);
    }

    public ICommand? CancelCommand
    {
        get => (ICommand?)GetValue(CancelCommandProperty);
        set => SetValue(CancelCommandProperty, value);
    }

    /// <summary>The cancel cap's word, Cancel unless the work stops some other way.</summary>
    public string CancelText
    {
        get => (string)GetValue(CancelTextProperty);
        set => SetValue(CancelTextProperty, value);
    }

    /// <summary>The share done as the panel shows it, clamped and rounded half up: "42%".</summary>
    public string PercentText
    {
        get => (string)GetValue(PercentTextProperty);
        private set => SetValue(PercentTextProperty, value);
    }

    /// <summary>The panel's serial, from its title as System-X's is.</summary>
    public string SerialText
    {
        get => (string)GetValue(SerialTextProperty);
        private set => SetValue(SerialTextProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        _sweep?.Detach();
        _sweep = null;

        base.OnApplyTemplate();
        _bar = GetTemplateChild("PART_Bar") as ProgressBar;
        _lamp = GetTemplateChild("PART_Lamp") as Control;
        if (GetTemplateChild("PART_ScanArea") is FrameworkElement area && GetTemplateChild("PART_ScanBand") is FrameworkElement band)
        {
            _sweep = new ScanSweep(area, band);
            if (IsLoaded)
                _sweep.Start();
        }

        UpdateStates();
    }

    private static void OnLookChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((BusyPanel)owner).UpdateStates();

    private bool IsDeterminate => double.IsFinite(Value);

    private void UpdateStates()
    {
        var title = Title ?? string.Empty;
        SerialText = PanelSerial.For(title);

        // The bar is set from here, and only ever to a finite share: WinUI's range controls refuse a value that is
        // not a number (RangeBase::put_Value), which is why the template does not bind it to Value.
        var share = IsDeterminate ? Math.Clamp(Value, 0, 100) : 0;
        PercentText = IsDeterminate
            ? Math.Round(share, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture) + "%"
            : string.Empty;
        if (_bar is not null)
        {
            _bar.Value = share;
            AutomationProperties.SetName(_bar, title);
        }

        if (_lamp is not null)
            AutomationProperties.SetName(_lamp, title);

        VisualStateManager.GoToState(this, string.IsNullOrEmpty(Detail) ? "NoDetail" : "HasDetail", false);
        VisualStateManager.GoToState(this, IsDeterminate ? "Determinate"
            : string.IsNullOrEmpty(Readout) ? "Indeterminate" : "IndeterminateReadout", false);
        VisualStateManager.GoToState(this, CancelCommand is null ? "NotCancellable" : "Cancellable", false);
    }
}
