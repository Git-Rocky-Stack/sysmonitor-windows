using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SysMonitor.Core.Models;
using Windows.UI;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// A rack module plate: a row or a small card seated flush in the rack, in the faceplate's brushed material but
/// without its bolts, stripe or deep shadow (System-X styles.css :1121-1247).
/// <para>
/// A plate that carries a condition reports it on a light pipe down its mounting edge, a rail 3 wide, rather than
/// by tinting its face: a rail reads down a list of forty at a glance, and the face keeps the luminance its text
/// was contrast-checked against. <see cref="LampState.Off"/> is the neutral plate, with no rail. A warn rail
/// blinks and a NO-GO rail holds steady, which is what tells them apart - their reds are one step apart, too
/// close to read on a rail 3 wide - and the blink rides the rail alone, so the words on the plate never dim. With
/// animation effects off, the warn rail holds lit. Armed is the one state that also fills the face: rows the user
/// has staged for a destructive run. The rail supplements a word, never replaces it: a row with a condition also
/// says it, in a lamp or a chip.
/// </para>
/// </summary>
public sealed class Plate : ContentControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(LampState), typeof(Plate), new PropertyMetadata(LampState.Off, OnLookChanged));

    public static readonly DependencyProperty IsPressableProperty = DependencyProperty.Register(
        nameof(IsPressable), typeof(bool), typeof(Plate), new PropertyMetadata(false, OnPressableChanged));

    public static readonly DependencyProperty ShadowColorProperty = DependencyProperty.Register(
        nameof(ShadowColor), typeof(Color), typeof(Plate), new PropertyMetadata(default(Color), OnLookChanged));

    public static readonly DependencyProperty RaisedShadowColorProperty = DependencyProperty.Register(
        nameof(RaisedShadowColor), typeof(Color), typeof(Plate), new PropertyMetadata(default(Color), OnLookChanged));

    private readonly ShadowPart _shadow;
    private bool _pointerOver;

    public Plate()
    {
        _shadow = new ShadowPart(this, 2, () => IsRaised
            ? [new ShadowLayer(2, 8, 0, RaisedShadowColor)]
            : [new ShadowLayer(1, 2, 0, ShadowColor)]);
        Loaded += (_, _) => UpdateStates();
    }

    public LampState State
    {
        get => (LampState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>
    /// A plate the user presses, inside whatever handles the press, such as a list item: the pointer becomes a
    /// hand over it, and it lifts on a deeper shadow, leaving the border alone because the border matches the
    /// rail.
    /// </summary>
    public bool IsPressable
    {
        get => (bool)GetValue(IsPressableProperty);
        set => SetValue(IsPressableProperty, value);
    }

    /// <summary>The shadow a plate sits on, <c>0 1px 2px</c>.</summary>
    public Color ShadowColor
    {
        get => (Color)GetValue(ShadowColorProperty);
        set => SetValue(ShadowColorProperty, value);
    }

    /// <summary>The deeper shadow of a pressable plate under the pointer, <c>0 2px 8px</c>.</summary>
    public Color RaisedShadowColor
    {
        get => (Color)GetValue(RaisedShadowColorProperty);
        set => SetValue(RaisedShadowColorProperty, value);
    }

    /// <summary>The shadows drawn now, for the smoke run to count.</summary>
    internal int ShadowLayers => _shadow.LayerCount;

    private bool IsRaised => IsPressable && _pointerOver;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _shadow.Attach(GetTemplateChild("PART_ShadowHost") as FrameworkElement);
        UpdateStates();
    }

    protected override void OnPointerEntered(PointerRoutedEventArgs e)
    {
        base.OnPointerEntered(e);
        _pointerOver = true;
        UpdateStates();
    }

    protected override void OnPointerExited(PointerRoutedEventArgs e)
    {
        base.OnPointerExited(e);
        _pointerOver = false;
        UpdateStates();
    }

    private static void OnLookChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((Plate)owner).UpdateStates();

    private static void OnPressableChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var plate = (Plate)owner;
        plate.ProtectedCursor = (bool)args.NewValue ? InputSystemCursor.Create(InputSystemCursorShape.Hand) : null;
        plate.UpdateStates();
    }

    private void UpdateStates()
    {
        var state = State == LampState.Warn && Motion.IsReduced ? "WarnSteady" : State.ToString();
        VisualStateManager.GoToState(this, state, false);
        VisualStateManager.GoToState(this, IsRaised ? "PointerOver" : "Normal", false);
        _shadow.Refresh();
    }
}
