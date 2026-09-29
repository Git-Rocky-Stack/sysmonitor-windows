using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SysMonitor.Core.Models;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// A VU meter: a cascade of segments running green, amber, orange, red (System-X src/components/console/Vu.tsx;
/// styles.css :1895-1943), with the warm zones packed into the top of the scale (<see cref="VuScale"/>).
/// Sixteen segments read as an instrument; fewer than eight read as a progress bar.
/// <para>
/// The highest lit segment flickers, the way a real meter's tip does, which is also the honest sign that the
/// reading is being sampled rather than parked. A settled reading that is not sampled live sets
/// <see cref="IsStatic"/>, because a flickering tip on a frozen number would be a lie about the data; the
/// flicker stops too when Windows' animation effects are off. <see cref="RangeBase.Value"/> runs from
/// <see cref="RangeBase.Minimum"/> to <see cref="RangeBase.Maximum"/>, 0 to 100 unless set; a reading outside
/// them is clamped, and one that is not a number lights nothing.
/// </para>
/// </summary>
public sealed class VuMeter : RangeBase
{
    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(int), typeof(VuMeter), new PropertyMetadata(16, OnLayoutChanged));

    public static readonly DependencyProperty IsVerticalProperty = DependencyProperty.Register(
        nameof(IsVertical), typeof(bool), typeof(VuMeter), new PropertyMetadata(false, OnLayoutChanged));

    public static readonly DependencyProperty IsStaticProperty = DependencyProperty.Register(
        nameof(IsStatic), typeof(bool), typeof(VuMeter), new PropertyMetadata(false, OnLitChanged));

    public static readonly DependencyProperty SegmentOffBrushProperty = DependencyProperty.Register(
        nameof(SegmentOffBrush), typeof(Brush), typeof(VuMeter), new PropertyMetadata(null, OnLitChanged));

    public static readonly DependencyProperty SegmentGoBrushProperty = DependencyProperty.Register(
        nameof(SegmentGoBrush), typeof(Brush), typeof(VuMeter), new PropertyMetadata(null, OnLitChanged));

    public static readonly DependencyProperty SegmentHoldBrushProperty = DependencyProperty.Register(
        nameof(SegmentHoldBrush), typeof(Brush), typeof(VuMeter), new PropertyMetadata(null, OnLitChanged));

    public static readonly DependencyProperty SegmentOrangeBrushProperty = DependencyProperty.Register(
        nameof(SegmentOrangeBrush), typeof(Brush), typeof(VuMeter), new PropertyMetadata(null, OnLitChanged));

    public static readonly DependencyProperty SegmentRedBrushProperty = DependencyProperty.Register(
        nameof(SegmentRedBrush), typeof(Brush), typeof(VuMeter), new PropertyMetadata(null, OnLitChanged));

    private readonly List<Border> _cells = new();
    private Grid? _track;
    private Storyboard? _flicker;
    private int _tip = -1;

    public VuMeter()
    {
        Maximum = 100;
        IsTabStop = false;
        Loaded += (_, _) => UpdateLit();
        Unloaded += (_, _) => StopFlicker();
    }

    public int Segments
    {
        get => (int)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    /// <summary>Stand the meter up, filling from the bottom.</summary>
    public bool IsVertical
    {
        get => (bool)GetValue(IsVerticalProperty);
        set => SetValue(IsVerticalProperty, value);
    }

    /// <summary>A settled reading, not sampled live: no flickering tip.</summary>
    public bool IsStatic
    {
        get => (bool)GetValue(IsStaticProperty);
        set => SetValue(IsStaticProperty, value);
    }

    // The zone colours come from the style, as theme resources, so the meter takes High Contrast's like any
    // other instrument; code asking Application.Resources would only ever get the application's theme.

    public Brush? SegmentOffBrush
    {
        get => (Brush?)GetValue(SegmentOffBrushProperty);
        set => SetValue(SegmentOffBrushProperty, value);
    }

    public Brush? SegmentGoBrush
    {
        get => (Brush?)GetValue(SegmentGoBrushProperty);
        set => SetValue(SegmentGoBrushProperty, value);
    }

    public Brush? SegmentHoldBrush
    {
        get => (Brush?)GetValue(SegmentHoldBrushProperty);
        set => SetValue(SegmentHoldBrushProperty, value);
    }

    public Brush? SegmentOrangeBrush
    {
        get => (Brush?)GetValue(SegmentOrangeBrushProperty);
        set => SetValue(SegmentOrangeBrushProperty, value);
    }

    public Brush? SegmentRedBrush
    {
        get => (Brush?)GetValue(SegmentRedBrushProperty);
        set => SetValue(SegmentRedBrushProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _track = GetTemplateChild("PART_Segments") as Grid;
        BuildSegments();
    }

    protected override void OnValueChanged(double oldValue, double newValue)
    {
        base.OnValueChanged(oldValue, newValue);
        UpdateLit();
    }

    protected override void OnMinimumChanged(double oldMinimum, double newMinimum)
    {
        base.OnMinimumChanged(oldMinimum, newMinimum);
        UpdateLit();
    }

    protected override void OnMaximumChanged(double oldMaximum, double newMaximum)
    {
        base.OnMaximumChanged(oldMaximum, newMaximum);
        UpdateLit();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new VuMeterAutomationPeer(this);

    private static void OnLayoutChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((VuMeter)owner).BuildSegments();

    private static void OnLitChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((VuMeter)owner).UpdateLit();

    private void BuildSegments()
    {
        VisualStateManager.GoToState(this, IsVertical ? "Vertical" : "Horizontal", false);
        if (_track is null)
            return;

        StopFlicker();
        _track.Children.Clear();
        _track.ColumnDefinitions.Clear();
        _track.RowDefinitions.Clear();
        _cells.Clear();

        var count = Math.Max(1, Segments);
        for (var i = 0; i < count; i++)
        {
            var cell = new Border { CornerRadius = new CornerRadius(1) };
            if (IsVertical)
            {
                _track.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(cell, count - 1 - i);
            }
            else
            {
                _track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(cell, i);
            }

            _track.Children.Add(cell);
            _cells.Add(cell);
        }

        UpdateLit();
    }

    private void UpdateLit()
    {
        if (_cells.Count == 0)
            return;

        var lit = VuScale.Lit(Value, Minimum, Maximum, _cells.Count);
        for (var i = 0; i < _cells.Count; i++)
            _cells[i].Background = i < lit ? ZoneBrush(VuScale.ZoneOf(i, _cells.Count)) : SegmentOffBrush;

        var tip = lit > 0 && !IsStatic && IsLoaded && !Motion.IsReduced ? lit - 1 : -1;
        if (tip == _tip)
            return;

        StopFlicker();
        if (tip >= 0)
            StartFlicker(tip);
    }

    private Brush? ZoneBrush(VuZone zone) => zone switch
    {
        VuZone.Red => SegmentRedBrush,
        VuZone.Orange => SegmentOrangeBrush,
        VuZone.Hold => SegmentHoldBrush,
        _ => SegmentGoBrush,
    };

    /// <summary>The tip's flicker: full, then 35%, 220 ms each, while it is the tip (:1934, :479-486).</summary>
    private void StartFlicker(int tip)
    {
        var flicker = new DoubleAnimationUsingKeyFrames
        {
            Duration = new Duration(TimeSpan.FromMilliseconds(440)),
            RepeatBehavior = RepeatBehavior.Forever,
        };
        flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = 1 });
        flicker.KeyFrames.Add(new DiscreteDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)),
            Value = 0.35,
        });
        Storyboard.SetTarget(flicker, _cells[tip]);
        Storyboard.SetTargetProperty(flicker, nameof(UIElement.Opacity));

        _flicker = new Storyboard();
        _flicker.Children.Add(flicker);
        _flicker.Begin();
        _tip = tip;
    }

    private void StopFlicker()
    {
        _flicker?.Stop();
        _flicker = null;
        _tip = -1;
    }

    /// <summary>
    /// Read as a progress bar, the closest thing UI Automation has to a meter, and called Level unless named.
    /// </summary>
    private sealed class VuMeterAutomationPeer(VuMeter owner) : RangeBaseAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;

        protected override string GetClassNameCore() => nameof(VuMeter);

        protected override string GetNameCore()
        {
            var named = AutomationProperties.GetName(Owner);
            return string.IsNullOrEmpty(named) ? "Level" : named;
        }
    }
}
