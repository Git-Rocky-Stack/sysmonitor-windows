using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// The two glows the bat-lever switch carries when it is armed: <c>0 0 12px var(--armed-glow)</c> outside the
/// track and <c>0 0 10px</c> outside the thumb (System-X styles.css .switch[data-on], :2352 and :2359).
/// <para>
/// Everything else about the lever is drawn by its template. These two cannot be: a CSS box-shadow with no
/// offset and a blur is light cast outside the box, and WinUI has no property for one. Every glow on this
/// console is a composition drop shadow attached from code (<see cref="CastShadow"/>, <see cref="ShadowPart"/>),
/// and code needs something per switch to run in.
/// </para>
/// <para>
/// It is an attached property rather than a subclass because <see cref="ToggleSwitch"/> is sealed - so the
/// glows are hung on the control from outside, and the style turns them on for every switch
/// (Controls.xaml:188). That is the better shape anyway: a subclass would have needed its own implicit style,
/// implicit styles key on the element's exact type, and the eleven switches on the four pages that have one
/// would have had to be rewritten to ask for it. A switch that asks for nothing is still a bat lever, which is
/// what the implicit style is for.
/// </para>
/// <para>
/// The colour is not decided here. The template carries the palette's armed glow on <c>PART_ArmedGlow</c> and
/// this reads it, so a change of shift arrives as a change of that brush and relights both glows without this
/// ever knowing which shift it is in - the same way a lamp's outer glow is coloured.
/// </para>
/// </summary>
public static class ConsoleLever
{
    /// <summary>Set by the switch's style: this switch draws the armed lever's two glows.</summary>
    public static readonly DependencyProperty HasGlowsProperty = DependencyProperty.RegisterAttached(
        "HasGlows", typeof(bool), typeof(ConsoleLever), new PropertyMetadata(false, OnHasGlowsChanged));

    /// <summary>The glows themselves, kept for the life of the switch that carries them.</summary>
    private static readonly DependencyProperty GlowsProperty = DependencyProperty.RegisterAttached(
        "Glows", typeof(LeverGlows), typeof(ConsoleLever), new PropertyMetadata(null));

    public static void SetHasGlows(DependencyObject element, bool value) =>
        element.SetValue(HasGlowsProperty, value);

    public static bool GetHasGlows(DependencyObject element) =>
        (bool)element.GetValue(HasGlowsProperty);

    /// <summary>How many layers each glow draws now, for the smoke run to count.</summary>
    internal static (int Track, int Thumb) GlowLayers(DependencyObject element) =>
        element.GetValue(GlowsProperty) is LeverGlows glows ? glows.LayerCounts : (0, 0);

    private static void OnHasGlowsChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is not ToggleSwitch lever)
            return;

        if (args.NewValue is true && lever.GetValue(GlowsProperty) is null)
            lever.SetValue(GlowsProperty, new LeverGlows(lever));
    }

    /// <summary>
    /// The pair of shadows on one switch. The hosts are found by walking the applied template rather than
    /// through <c>GetTemplateChild</c>, which only the control itself can call - so the walk waits for the
    /// switch to load, which is also when a composition shadow can first be built.
    /// </summary>
    private sealed class LeverGlows
    {
        /// <summary>The track's blur, on the 4px control radius.</summary>
        private const float TrackBlur = 12;

        /// <summary>The thumb's, on the 3px inset radius.</summary>
        private const float ThumbBlur = 10;

        private readonly ToggleSwitch _lever;
        private readonly ShadowPart _track;
        private readonly ShadowPart _thumb;
        private Border? _colour;
        private long _colourToken;

        public LeverGlows(ToggleSwitch lever)
        {
            _lever = lever;
            _track = new ShadowPart(lever, 4, () => Lit(TrackBlur));
            _thumb = new ShadowPart(lever, 3, () => Lit(ThumbBlur));

            lever.Loaded += (_, _) => Attach();
            lever.Unloaded += (_, _) => Detach();

            // Armed is the only lit state, and a locked lever is not lit: CSS dims the whole control when it is
            // disabled, which puts its glows out with it.
            lever.Toggled += (_, _) => Refresh();
            lever.IsEnabledChanged += (_, _) => Refresh();
        }

        public (int Track, int Thumb) LayerCounts => (_track.LayerCount, _thumb.LayerCount);

        private void Attach()
        {
            Detach();

            _colour = Find<Border>("PART_ArmedGlow");
            if (_colour is not null)
                _colourToken = _colour.RegisterPropertyChangedCallback(Border.BackgroundProperty,
                    (_, _) => Refresh());

            _track.Attach(Find<FrameworkElement>("PART_TrackGlowHost"));

            // The thumb's host rides inside SwitchKnob, so its glow travels with the thumb rather than staying
            // behind at the left of the track.
            _thumb.Attach(Find<FrameworkElement>("PART_ThumbGlowHost"));
            Refresh();
        }

        private void Detach()
        {
            if (_colour is not null)
                _colour.UnregisterPropertyChangedCallback(Border.BackgroundProperty, _colourToken);

            _colour = null;
        }

        private void Refresh()
        {
            _track.Refresh();
            _thumb.Refresh();
        }

        /// <summary>The one layer a glow draws, or none when the lever is not armed and lit.</summary>
        private IEnumerable<ShadowLayer> Lit(float blur) =>
            _lever.IsOn && _lever.IsEnabled
                ? [new ShadowLayer(0, blur, 0, (_colour?.Background as SolidColorBrush)?.Color ?? default(Color))]
                : [];

        /// <summary>The one element of this name in the switch's applied template.</summary>
        private T? Find<T>(string name) where T : FrameworkElement
        {
            var pending = new Queue<DependencyObject>();
            pending.Enqueue(_lever);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                if (current is T found && string.Equals(found.Name, name, StringComparison.Ordinal))
                    return found;

                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(current); i++)
                    pending.Enqueue(VisualTreeHelper.GetChild(current, i));
            }

            return null;
        }
    }
}
