using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// Phosphor: light around something lit, drawn by composition - the glow round an LCD's reading and a lamp's word
/// (CSS <c>text-shadow: 0 0 8px</c> and <c>0 0 5px</c>) and the halo of an LED (<c>box-shadow: 0 0 6px 1px</c>),
/// System-X styles.css :1698, :1799, :1877. It is a drop shadow with no offset whose mask is the lit element's
/// own alpha, so it follows the letters or the dot exactly, drawn on a host that sits under the element in its
/// template and over whatever face it is lit against.
/// <para>
/// Glows are off in High Contrast, and inside a list item: a list of fifty rows would build fifty masks, the wrong
/// price for a halo. A colour with no alpha puts the glow out, which is how an unlit lamp shows none.
/// </para>
/// </summary>
internal sealed class Glow : IDisposable
{
    private static readonly AccessibilitySettings Accessibility = new();

    private readonly FrameworkElement _host;
    private readonly FrameworkElement _source;
    private readonly float _spread;
    private readonly SpriteVisual _sprite;
    private readonly DropShadow _shadow;
    private readonly bool _allowed;

    /// <param name="host">An element under <paramref name="source"/> in the template, covering it.</param>
    /// <param name="source">The lit text or shape whose outline glows.</param>
    /// <param name="blur">CSS's blur radius.</param>
    /// <param name="spread">CSS's spread, which widens the mask by this much on every side.</param>
    public Glow(FrameworkElement host, FrameworkElement source, float blur, float spread = 0)
    {
        _host = host;
        _source = source;
        _spread = spread;

        var compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _shadow = compositor.CreateDropShadow();
        _shadow.BlurRadius = blur;
        _shadow.Offset = Vector3.Zero;
        _shadow.Mask = source switch
        {
            TextBlock text => text.GetAlphaMask(),
            Shape shape => shape.GetAlphaMask(),
            _ => null,
        };

        _sprite = compositor.CreateSpriteVisual();
        _sprite.Shadow = _shadow;
        _sprite.IsVisible = false;
        ElementCompositionPreview.SetElementChildVisual(host, _sprite);

        _allowed = _shadow.Mask is not null && !InListItem(source);
        _source.SizeChanged += OnSizeChanged;
        _host.SizeChanged += OnSizeChanged;
        Place();
    }

    /// <summary>Whether the glow is drawn now.</summary>
    public bool IsLit => _sprite.IsVisible;

    /// <summary>Lights the glow in this colour; a colour with no alpha, or High Contrast, puts it out.</summary>
    public void Light(Color color)
    {
        _shadow.Color = color;
        _sprite.IsVisible = _allowed && color.A > 0 && !Accessibility.HighContrast;
    }

    public void Dispose()
    {
        _source.SizeChanged -= OnSizeChanged;
        _host.SizeChanged -= OnSizeChanged;
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        _shadow.Mask?.Dispose();
        _shadow.Dispose();
        _sprite.Dispose();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Place();

    /// <summary>Lays the sprite over the source, where it sits within the host, widened by the spread.</summary>
    private void Place()
    {
        var origin = _source.TransformToVisual(_host).TransformPoint(new Point(0, 0));
        _sprite.Offset = new Vector3((float)origin.X - _spread, (float)origin.Y - _spread, 0);
        _sprite.Size = new Vector2((float)_source.ActualWidth + 2 * _spread, (float)_source.ActualHeight + 2 * _spread);
    }

    /// <summary>Whether the element is drawn inside a list's item.</summary>
    private static bool InListItem(DependencyObject element)
    {
        for (var current = VisualTreeHelper.GetParent(element); current is not null;
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is SelectorItem)
                return true;
        }

        return false;
    }
}
