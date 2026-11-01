using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// The light a lit bar of LEDs throws into the lens it sits behind: a glow round <see cref="Source"/>, the shape
/// that is lit, in that shape's own colour, drawn by composition (<see cref="Glow"/>) on this element, which a
/// template places under the shape and over the dark track. A meter's lit length is the case: its glow follows the
/// length as the meter fills, and bleeds into the dark LEDs beside it the way a real lens does.
/// <para>
/// It is light, not content: it takes no focus and a screen reader passes over it. Like every glow it is off in
/// High Contrast and inside a list's item, and a shape with no colour lights nothing.
/// </para>
/// </summary>
public sealed class LedBloom : Control
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(Shape), typeof(LedBloom), new PropertyMetadata(null, OnSourceChanged));

    private readonly GlowPart _bloom;

    public LedBloom()
    {
        IsTabStop = false;
        IsHitTestVisible = false;
        _bloom = new GlowPart(this, 7, 0, () => (Source?.Fill as SolidColorBrush)?.Color ?? default(Color));
    }

    /// <summary>The lit shape whose outline glows.</summary>
    public Shape? Source
    {
        get => (Shape?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    /// <summary>Whether the bloom is drawn now, for the smoke run to check.</summary>
    internal bool IsGlowing => _bloom.IsLit;

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        AttachBloom();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LedBloomAutomationPeer(this);

    private static void OnSourceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((LedBloom)owner).AttachBloom();

    private void AttachBloom() =>
        _bloom.Attach(GetTemplateChild("PART_GlowHost") as FrameworkElement, Source, Source, Shape.FillProperty);

    /// <summary>Light, not content: neither a control nor content to a screen reader.</summary>
    private sealed class LedBloomAutomationPeer(LedBloom owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(LedBloom);

        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;
    }
}
