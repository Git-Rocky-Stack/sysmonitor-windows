using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

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
/// <para>
/// It stands off the chassis on System-X's four shadows (:1059-1062): a hard line under its edge, a contact shadow,
/// the drop and a wide ambient one, each a colour from the palette, far lighter on Day Shift and none in High
/// Contrast. Composition draws them (<see cref="CastShadow"/>).
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

    public static readonly DependencyProperty EdgeShadowColorProperty = DependencyProperty.Register(
        nameof(EdgeShadowColor), typeof(Color), typeof(Faceplate),
        new PropertyMetadata(default(Color), OnShadowChanged));

    public static readonly DependencyProperty ContactShadowColorProperty = DependencyProperty.Register(
        nameof(ContactShadowColor), typeof(Color), typeof(Faceplate),
        new PropertyMetadata(default(Color), OnShadowChanged));

    public static readonly DependencyProperty DropShadowColorProperty = DependencyProperty.Register(
        nameof(DropShadowColor), typeof(Color), typeof(Faceplate),
        new PropertyMetadata(default(Color), OnShadowChanged));

    public static readonly DependencyProperty AmbientShadowColorProperty = DependencyProperty.Register(
        nameof(AmbientShadowColor), typeof(Color), typeof(Faceplate),
        new PropertyMetadata(default(Color), OnShadowChanged));

    private readonly ShadowPart _shadow;

    public Faceplate()
    {
        _shadow = new ShadowPart(this, 2, () =>
        [
            new ShadowLayer(1, 0, 0, EdgeShadowColor),
            new ShadowLayer(2, 4, 0, ContactShadowColor),
            new ShadowLayer(12, 28, 0, DropShadowColor),
            new ShadowLayer(40, 80, 0, AmbientShadowColor),
        ]);
    }

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

    /// <summary>The hard line along its lower edge, <c>0 1px 0</c>.</summary>
    public Color EdgeShadowColor
    {
        get => (Color)GetValue(EdgeShadowColorProperty);
        set => SetValue(EdgeShadowColorProperty, value);
    }

    /// <summary>The contact shadow, <c>0 2px 4px</c>.</summary>
    public Color ContactShadowColor
    {
        get => (Color)GetValue(ContactShadowColorProperty);
        set => SetValue(ContactShadowColorProperty, value);
    }

    /// <summary>The drop shadow, <c>0 12px 28px</c>.</summary>
    public Color DropShadowColor
    {
        get => (Color)GetValue(DropShadowColorProperty);
        set => SetValue(DropShadowColorProperty, value);
    }

    /// <summary>The wide ambient shadow, <c>0 40px 80px</c>.</summary>
    public Color AmbientShadowColor
    {
        get => (Color)GetValue(AmbientShadowColorProperty);
        set => SetValue(AmbientShadowColorProperty, value);
    }

    /// <summary>The shadows drawn now, for the smoke run to count.</summary>
    internal int ShadowLayers => _shadow.LayerCount;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _shadow.Attach(GetTemplateChild("PART_ShadowHost") as FrameworkElement);
        UpdateStates();
    }

    private static void OnLayoutChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((Faceplate)owner).UpdateStates();

    private static void OnShadowChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((Faceplate)owner)._shadow.Refresh();

    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsBare ? "Bare" : "Striped", false);
        VisualStateManager.GoToState(this, ShowBolts ? "Bolted" : "Unbolted", false);
    }
}
