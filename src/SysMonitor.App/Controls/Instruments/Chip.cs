using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.Core.Models;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// A chip: a small flat cap carrying a word in Departure Mono capitals, 24 high (System-X styles.css :2176-2228).
/// Where a lamp reports a condition, a chip carries a rank or a kind - a severity, a type, a count - so it never
/// blinks, even in the warn state: a rank that blinked like an alarm would claim to be one.
/// <para>
/// Its colours are the state-as-text tokens, which darken along their own hue on Day Shift so the word stays
/// readable on silver, and go back to the LEDs inside a well or display, which are dark in both shifts.
/// <see cref="LampState.Off"/> is the neutral chip. The word is its content, written in capitals.
/// </para>
/// </summary>
public sealed class Chip : ContentControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(LampState), typeof(Chip), new PropertyMetadata(LampState.Off, OnStateChanged));

    public LampState State
    {
        get => (LampState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        VisualStateManager.GoToState(this, State.ToString(), false);
    }

    private static void OnStateChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        VisualStateManager.GoToState((Chip)owner, ((LampState)args.NewValue).ToString(), false);
}
