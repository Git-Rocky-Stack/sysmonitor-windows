using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using SysMonitor.Core.Models;
using Windows.UI;

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
/// <para>
/// The cap sits on a small shadow; lit, it glows outside in its state's soft colour and round its word in its
/// LED's colour, at 55%, or 50% for the reds (:1697-1789). Composition draws both (<see cref="CastShadow"/>,
/// <see cref="Glow"/>), and the word's glow is left off inside a list item.
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

    public static readonly DependencyProperty ShadowColorProperty = DependencyProperty.Register(
        nameof(ShadowColor), typeof(Color), typeof(Lamp), new PropertyMetadata(default(Color), OnShadowChanged));

    private readonly ShadowPart _shadow;
    private readonly GlowPart _wordGlow;
    private Border? _outerGlow;
    private long _outerGlowToken;
    private TextBlock? _word;

    public Lamp()
    {
        _shadow = new ShadowPart(this, 4, () => State == LampState.Off
            ? [new ShadowLayer(1, 2, 0, ShadowColor)]
            : [new ShadowLayer(1, 2, 0, ShadowColor), new ShadowLayer(0, 10, 0, ColourOf(_outerGlow?.Background))]);
        _wordGlow = new GlowPart(this, 5, 0, WordGlowColour);
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

    /// <summary>The shadow the cap sits on, <c>0 1px 2px</c>.</summary>
    public Color ShadowColor
    {
        get => (Color)GetValue(ShadowColorProperty);
        set => SetValue(ShadowColorProperty, value);
    }

    /// <summary>The shadows and outer glow drawn now, for the smoke run to count.</summary>
    internal int ShadowLayers => _shadow.LayerCount;

    /// <summary>Whether the word glows now, for the smoke run to check.</summary>
    internal bool IsWordGlowing => _wordGlow.IsLit;

    protected override void OnApplyTemplate()
    {
        _outerGlow?.UnregisterPropertyChangedCallback(Border.BackgroundProperty, _outerGlowToken);

        base.OnApplyTemplate();
        _word = GetTemplateChild("Word") as TextBlock;
        _outerGlow = GetTemplateChild("PART_OuterGlow") as Border;
        if (_outerGlow is not null)
            _outerGlowToken = _outerGlow.RegisterPropertyChangedCallback(Border.BackgroundProperty,
                (_, _) => _shadow.Refresh());

        _shadow.Attach(GetTemplateChild("PART_ShadowHost") as FrameworkElement);
        _wordGlow.Attach(GetTemplateChild("PART_WordGlowHost") as FrameworkElement, _word, _word,
            TextBlock.ForegroundProperty);
        UpdateStates(strike: false);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LampAutomationPeer(this);

    private static void OnLookChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((Lamp)owner).UpdateStates(strike: false);

    private static void OnShadowChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((Lamp)owner)._shadow.Refresh();

    private void UpdateStates(bool strike)
    {
        var state = State == LampState.Warn && Motion.IsReduced ? "WarnSteady" : State.ToString();
        VisualStateManager.GoToState(this, state, false);
        VisualStateManager.GoToState(this, Size.ToString(), false);
        if (strike && State != LampState.Off && !Motion.IsReduced)
            VisualStateManager.GoToState(this, "Striking", false);

        _shadow.Refresh();
        _wordGlow.Refresh();
    }

    /// <summary>
    /// The word's glow: its LED colour at 55%, or 50% for the reds, as System-X's <c>--lamp-glow</c>; none unlit.
    /// </summary>
    private Color WordGlowColour()
    {
        if (State == LampState.Off || ColourOf(_word?.Foreground) is not { A: > 0 } lit)
            return default;

        var share = State is LampState.Warn or LampState.NoGo or LampState.Armed ? 0.5 : 0.55;
        return Color.FromArgb((byte)Math.Round(255 * share, MidpointRounding.AwayFromZero), lit.R, lit.G, lit.B);
    }

    private static Color ColourOf(Brush? brush) => (brush as SolidColorBrush)?.Color ?? default;

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
