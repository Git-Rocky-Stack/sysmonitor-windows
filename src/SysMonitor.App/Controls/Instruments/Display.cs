using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>The tint a <see cref="Display"/> lays over its dark ground to report a result.</summary>
public enum DisplayTone
{
    None,
    Go,
    NoGo,
}

/// <summary>
/// A dark display: the ground of a readout that is not a recessed well - a result, a summary line - dark in
/// both shifts like every display (System-X styles.css :1427-1449).
/// <para>
/// System-X's <c>.display</c> once painted no ground of its own and left its text unreadable on Day Shift, so
/// this one always paints the display gradient, and sets its own theme to Dark for the same reason a
/// <see cref="Well"/> does. <see cref="Tone"/> lays the go or no-go tint over that ground rather than in place
/// of it: a tint alone over the silver chassis would be a pale green chassis, not a display.
/// </para>
/// </summary>
public sealed class Display : ContentControl
{
    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(DisplayTone), typeof(Display), new PropertyMetadata(DisplayTone.None, OnToneChanged));

    public Display()
    {
        RequestedTheme = ElementTheme.Dark;
    }

    public DisplayTone Tone
    {
        get => (DisplayTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateTone();
    }

    private static void OnToneChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((Display)owner).UpdateTone();

    private void UpdateTone() => VisualStateManager.GoToState(this, Tone.ToString(), false);
}
