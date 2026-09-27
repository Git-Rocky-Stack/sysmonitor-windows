using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// Layer 1 of the console's depth: a raised brushed-aluminium faceplate on the carbon chassis, the panel every
/// major section of a page is built on (System-X src/components/console/Faceplate.tsx; styles.css :1037-1119).
/// Four corner bolts, a brushed stripe carrying the panel's kicker and serial, then the body. Wells carve into
/// it and caps sit on it.
/// <para>
/// The body is padded to clear the bolts (<c>FaceplateBodyPadding</c>); a body that is its own grid sets
/// <see cref="Control.Padding"/> to 0. The body row takes whatever height is left, so a list hosted in it keeps
/// its virtualisation.
/// </para>
/// </summary>
public sealed class Faceplate : ContentControl
{
    public static readonly DependencyProperty KickerProperty = DependencyProperty.Register(
        nameof(Kicker), typeof(string), typeof(Faceplate), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SerialProperty = DependencyProperty.Register(
        nameof(Serial), typeof(string), typeof(Faceplate), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty StripeRightProperty = DependencyProperty.Register(
        nameof(StripeRight), typeof(object), typeof(Faceplate), new PropertyMetadata(null));

    public static readonly DependencyProperty IsBareProperty = DependencyProperty.Register(
        nameof(IsBare), typeof(bool), typeof(Faceplate), new PropertyMetadata(false, OnLayoutChanged));

    public static readonly DependencyProperty ShowBoltsProperty = DependencyProperty.Register(
        nameof(ShowBolts), typeof(bool), typeof(Faceplate), new PropertyMetadata(true, OnLayoutChanged));

    /// <summary>
    /// The stripe's kicker, in Departure Mono caps: conventionally <c>MODULE - NAME - 01A</c>, the panel's code.
    /// Written in capitals, because the source string is what the documentation quotes.
    /// </summary>
    public string Kicker
    {
        get => (string)GetValue(KickerProperty);
        set => SetValue(KickerProperty, value);
    }

    /// <summary>
    /// The stripe's serial. <see cref="SysMonitor.Core.Helpers.PanelSerial"/> gives a module a stable one.
    /// </summary>
    public string Serial
    {
        get => (string)GetValue(SerialProperty);
        set => SetValue(SerialProperty, value);
    }

    /// <summary>The stripe's right-hand slot: a lamp or two, a small readout, one action.</summary>
    public object? StripeRight
    {
        get => GetValue(StripeRightProperty);
        set => SetValue(StripeRightProperty, value);
    }

    /// <summary>No stripe: a bare plate, for sections that are not panels in their own right.</summary>
    public bool IsBare
    {
        get => (bool)GetValue(IsBareProperty);
        set => SetValue(IsBareProperty, value);
    }

    /// <summary>The corner bolts. System-X leaves them off panels narrower than about 180.</summary>
    public bool ShowBolts
    {
        get => (bool)GetValue(ShowBoltsProperty);
        set => SetValue(ShowBoltsProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateStates();
    }

    private static void OnLayoutChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((Faceplate)owner).UpdateStates();

    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsBare ? "Bare" : "Striped", false);
        VisualStateManager.GoToState(this, ShowBolts ? "Bolted" : "Unbolted", false);
    }
}
