using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using SysMonitor.Core.Models;
using Windows.UI;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// The ACT light on a process's progress meter: an LED behind its lens that reads the meter it sits on
/// (<see cref="Meter"/>) through <see cref="ActivityLight"/>. Dark until the meter is shown; the moment the work
/// begins it strikes - three quick flashes - then flickers irregularly while the work runs, as a drive's activity
/// light does; it holds lit once the meter is full. A meter falling back to start a new run strikes it again.
/// <para>
/// With Windows' animation effects off it never flickers: busy and done both hold it lit, which still says the
/// work is under way. It is decoration beside a meter that is itself read out, so a screen reader passes over it.
/// Lit, it has the LED's halo (<see cref="Glow"/>), which dims with the emitter it is drawn under.
/// </para>
/// </summary>
public sealed class ActivityLed : Control
{
    public static readonly DependencyProperty MeterProperty = DependencyProperty.Register(
        nameof(Meter), typeof(RangeBase), typeof(ActivityLed), new PropertyMetadata(null, OnMeterChanged));

    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(LampState), typeof(ActivityLed), new PropertyMetadata(LampState.Go, OnStateChanged));

    private readonly GlowPart _halo;
    private UIElement? _emitter;
    private Shape? _light;
    private Storyboard? _timeline;
    private ActivityPhase _phase = ActivityPhase.Dark;
    private double _lastValue = double.NaN;
    private long _visibilityToken;
    private long _indeterminateToken;
    private int _generation;

    public ActivityLed()
    {
        IsTabStop = false;
        _halo = new GlowPart(this, 6, 1, () =>
            _phase == ActivityPhase.Dark ? default : ((_light?.Fill as SolidColorBrush)?.Color ?? default(Color)));
        Loaded += (_, _) => Update();
        Unloaded += (_, _) =>
        {
            Stop();
            _phase = ActivityPhase.Dark;
        };
    }

    /// <summary>The meter whose work this light reports; usually the progress bar whose template holds it.</summary>
    public RangeBase? Meter
    {
        get => (RangeBase?)GetValue(MeterProperty);
        set => SetValue(MeterProperty, value);
    }

    /// <summary>The LED's colour, as a lamp state names it. Go unless set.</summary>
    public LampState State
    {
        get => (LampState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>What the light is doing now, for the smoke run to check.</summary>
    internal ActivityPhase Phase => _phase;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        Stop();
        _emitter = GetTemplateChild("PART_Emitter") as UIElement;
        _light = GetTemplateChild("Light") as Shape;
        _halo.Attach(GetTemplateChild("PART_GlowHost") as FrameworkElement, _light, _light, Shape.FillProperty);
        GoToTone();
        _phase = ActivityPhase.Dark;
        Update();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ActivityLedAutomationPeer(this);

    private static void OnMeterChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var led = (ActivityLed)owner;
        led.Unwatch(args.OldValue as RangeBase);
        led.Watch(args.NewValue as RangeBase);
        led.Update();
    }

    private static void OnStateChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((ActivityLed)owner).GoToTone();

    private void GoToTone()
    {
        // Off has no colour to light in; the light keeps the go colour rather than lighting graphite.
        var tone = State == LampState.Off ? LampState.Go : State;
        VisualStateManager.GoToState(this, tone.ToString(), false);
        _halo.Refresh();
    }

    private void Watch(RangeBase? meter)
    {
        if (meter is null)
            return;

        meter.ValueChanged += OnMeterValueChanged;
        _visibilityToken = meter.RegisterPropertyChangedCallback(VisibilityProperty, OnMeterPropertyChanged);
        if (meter is ProgressBar bar)
            _indeterminateToken = bar.RegisterPropertyChangedCallback(ProgressBar.IsIndeterminateProperty, OnMeterPropertyChanged);
    }

    private void Unwatch(RangeBase? meter)
    {
        if (meter is null)
            return;

        meter.ValueChanged -= OnMeterValueChanged;
        meter.UnregisterPropertyChangedCallback(VisibilityProperty, _visibilityToken);
        if (meter is ProgressBar bar)
            bar.UnregisterPropertyChangedCallback(ProgressBar.IsIndeterminateProperty, _indeterminateToken);
    }

    private void OnMeterValueChanged(object sender, RangeBaseValueChangedEventArgs e) => Update();

    private void OnMeterPropertyChanged(DependencyObject sender, DependencyProperty property) => Update();

    private void Update()
    {
        var meter = Meter;
        var showing = IsLoaded && meter is not null && meter.Visibility == Visibility.Visible;
        var indeterminate = meter is ProgressBar { IsIndeterminate: true };
        var value = meter?.Value ?? double.NaN;
        var phase = ActivityLight.PhaseOf(showing, indeterminate, value, meter?.Minimum ?? 0, meter?.Maximum ?? 0);
        var strikes = ActivityLight.Strikes(_phase, phase, _lastValue, value);
        _lastValue = value;
        var was = _phase;
        _phase = phase;

        if (_emitter is null)
            return;

        switch (phase)
        {
            case ActivityPhase.Busy when !Motion.IsReduced:
                if (strikes || was != ActivityPhase.Busy || _timeline is null)
                    Run(strikes);
                break;

            case ActivityPhase.Dark:
                Stop();
                _emitter.Opacity = 0;
                break;

            default:
                // Done, or busy with animation effects off: held lit.
                Stop();
                _emitter.Opacity = 1;
                break;
        }

        _halo.Refresh();
    }

    /// <summary>Runs the strike, when the work has just begun, and then the flicker for as long as it runs.</summary>
    private void Run(bool strike)
    {
        Stop();
        var generation = _generation;
        if (!strike)
        {
            Begin(ActivityLight.Cadence, ActivityLight.CadenceDuration, forever: true);
            return;
        }

        Begin(ActivityLight.Strike, ActivityLight.StrikeDuration, forever: false);
        _timeline!.Completed += (_, _) =>
        {
            // A strike that was stopped, or overtaken by another, hands over to nothing.
            if (generation == _generation && _phase == ActivityPhase.Busy && IsLoaded)
                Begin(ActivityLight.Cadence, ActivityLight.CadenceDuration, forever: true);
        };
    }

    private void Begin(IReadOnlyList<ActivityBlink> keys, TimeSpan duration, bool forever)
    {
        var frames = new DoubleAnimationUsingKeyFrames { Duration = new Duration(duration) };
        if (forever)
            frames.RepeatBehavior = RepeatBehavior.Forever;

        foreach (var key in keys)
        {
            frames.KeyFrames.Add(new DiscreteDoubleKeyFrame
            {
                KeyTime = KeyTime.FromTimeSpan(key.At),
                Value = key.Lit ? 1 : 0,
            });
        }

        Storyboard.SetTarget(frames, _emitter);
        Storyboard.SetTargetProperty(frames, nameof(UIElement.Opacity));
        _timeline = new Storyboard();
        _timeline.Children.Add(frames);
        _timeline.Begin();
    }

    private void Stop()
    {
        _generation++;
        _timeline?.Stop();
        _timeline = null;
    }

    /// <summary>Decoration beside a meter that reads itself out: not a control and not content.</summary>
    private sealed class ActivityLedAutomationPeer(ActivityLed owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(ActivityLed);

        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;
    }
}
