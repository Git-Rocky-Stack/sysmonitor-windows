using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.Core.Models;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>The two sizes a lamp comes in: the standard cap, and the small one stripes and banners carry.</summary>
public enum LampSize
{
    Medium,
    Small,
}

/// <summary>
/// A lamp tile: a dark cap carrying a stencil word, lit in its state's colour (System-X
/// src/components/console/Lamp.tsx; styles.css :1675-1789).
/// <para>
/// Status is a word first and a colour second, so a lamp reads to someone who cannot tell the LED hues apart,
/// and a screenshot needs no legend. The word is <see cref="Code"/>, two to five letters written in capitals: GO,
/// HOLD, NO-GO, EXEC, STBY, ON AIR. The cap stays dark in both shifts. A warn lamp blinks at 1 Hz until what it
/// warns about is dealt with - never a settled fault, which is <see cref="LampState.NoGo"/> and holds steady -
/// and holds lit instead when Windows' animation effects are off.
/// </para>
/// </summary>
public sealed class Lamp : Control
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(LampState), typeof(Lamp), new PropertyMetadata(LampState.Off, OnLookChanged));

    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(Lamp), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(LampSize), typeof(Lamp), new PropertyMetadata(LampSize.Medium, OnLookChanged));

    public static readonly DependencyProperty StrikeProperty = DependencyProperty.Register(
        nameof(Strike), typeof(bool), typeof(Lamp), new PropertyMetadata(false));

    public Lamp()
    {
        Loaded += (_, _) => UpdateStates(strike: Strike);
    }

    public LampState State
    {
        get => (LampState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The stencil word, in capitals as it is shown.</summary>
    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public LampSize Size
    {
        get => (LampSize)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>
    /// Ignite when shown: an 80 ms attack from dim to lit (System-X's strike, :439-452), for a state that has just
    /// gone live rather than one that was already lit. Skipped when animation effects are off.
    /// </summary>
    public bool Strike
    {
        get => (bool)GetValue(StrikeProperty);
        set => SetValue(StrikeProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateStates(strike: false);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LampAutomationPeer(this);

    private static void OnLookChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((Lamp)owner).UpdateStates(strike: false);

    private void UpdateStates(bool strike)
    {
        var state = State == LampState.Warn && Motion.IsReduced ? "WarnSteady" : State.ToString();
        VisualStateManager.GoToState(this, state, false);
        VisualStateManager.GoToState(this, Size.ToString(), false);
        if (strike && State != LampState.Off && !Motion.IsReduced)
            VisualStateManager.GoToState(this, "Striking", false);
    }

    /// <summary>What a screen reader says after the word: the state's meaning, in the LED vocabulary's words.</summary>
    internal static string Meaning(LampState state) => state switch
    {
        LampState.Go => "running",
        LampState.Hold => "caution",
        LampState.Warn => "unacknowledged warning",
        LampState.NoGo => "fault",
        LampState.Exec => "in progress",
        LampState.Armed => "live",
        _ => "off",
    };

    /// <summary>
    /// A lamp is read as text: its word and what its state means, "GAME, live", unless the page names it
    /// itself.
    /// </summary>
    private sealed class LampAutomationPeer(Lamp owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(Lamp);

        protected override string GetNameCore()
        {
            var lamp = (Lamp)Owner;
            var named = AutomationProperties.GetName(lamp);
            return string.IsNullOrEmpty(named) ? $"{lamp.Code}, {Meaning(lamp.State)}" : named;
        }
    }
}
