using System.Numerics;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>One shadow a box casts, as CSS writes it: <c>0 {OffsetY}px {Blur}px {Spread}px {Color}</c>.</summary>
internal readonly record struct ShadowLayer(float OffsetY, float Blur, float Spread, Color Color);

/// <summary>
/// The shadows a rounded rectangle casts outside itself, drawn by composition behind a part of a template: the
/// stacked CSS box-shadows WinUI has no property for, such as the faceplate's four (System-X styles.css
/// :1059-1062). Each layer is a layer visual casting a drop shadow from a rounded rectangle the host's size,
/// widened by the layer's spread; the rectangle itself is hidden behind the face the host sits under.
/// <para>
/// Nothing is drawn in High Contrast, where the console's decoration goes, and a layer whose colour has no alpha
/// is left out, which is how the palette turns one off. The owner builds it when it loads and disposes of it when
/// it unloads or takes a new template, so a control out of the tree holds no composition resources.
/// </para>
/// </summary>
internal sealed class CastShadow : IDisposable
{
    private static readonly AccessibilitySettings Accessibility = new();

    private readonly FrameworkElement _host;
    private readonly float _radius;
    private readonly Compositor _compositor;
    private readonly ContainerVisual _root;
    private readonly List<(ShadowLayer Layer, LayerVisual Visual, ShapeVisual Caster,
        CompositionRoundedRectangleGeometry Shape)> _layers = [];

    public CastShadow(FrameworkElement host, float radius)
    {
        _host = host;
        _radius = radius;
        _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _root = _compositor.CreateContainerVisual();
        ElementCompositionPreview.SetElementChildVisual(host, _root);
        _host.SizeChanged += OnHostSizeChanged;
    }

    /// <summary>The layers drawn now: none in High Contrast, and none for a colour with no alpha.</summary>
    public int LayerCount => _layers.Count;

    /// <summary>Draws these layers in place of whatever was drawn before.</summary>
    public void Show(IEnumerable<ShadowLayer> layers)
    {
        Clear();
        if (Accessibility.HighContrast)
            return;

        foreach (var layer in layers.Where(layer => layer.Color.A > 0))
        {
            var shape = _compositor.CreateRoundedRectangleGeometry();
            var fill = _compositor.CreateSpriteShape(shape);
            fill.FillBrush = _compositor.CreateColorBrush(Colors.Black);

            var caster = _compositor.CreateShapeVisual();
            caster.Shapes.Add(fill);

            var shadow = _compositor.CreateDropShadow();
            shadow.BlurRadius = layer.Blur;
            shadow.Offset = new Vector3(0, layer.OffsetY, 0);
            shadow.Color = layer.Color;

            var visual = _compositor.CreateLayerVisual();
            visual.Shadow = shadow;
            visual.Children.InsertAtTop(caster);

            // The widest shadow goes underneath, as the last of a CSS list paints first.
            _root.Children.InsertAtBottom(visual);
            _layers.Add((layer, visual, caster, shape));
        }

        Resize();
    }

    public void Dispose()
    {
        _host.SizeChanged -= OnHostSizeChanged;
        Clear();
        ElementCompositionPreview.SetElementChildVisual(_host, null);
        _root.Dispose();
    }

    private void OnHostSizeChanged(object sender, SizeChangedEventArgs e) => Resize();

    private void Resize()
    {
        var size = new Vector2((float)_host.ActualWidth, (float)_host.ActualHeight);
        _root.Size = size;
        foreach (var (layer, visual, caster, shape) in _layers)
        {
            var widened = size + new Vector2(2 * layer.Spread);
            visual.Size = widened;
            visual.Offset = new Vector3(-layer.Spread, -layer.Spread, 0);
            caster.Size = widened;
            shape.Size = widened;
            shape.CornerRadius = new Vector2(_radius + layer.Spread);
        }
    }

    private void Clear()
    {
        _root.Children.RemoveAll();
        foreach (var (_, visual, caster, shape) in _layers)
        {
            visual.Shadow?.Dispose();
            caster.Dispose();
            visual.Dispose();
            shape.Dispose();
        }

        _layers.Clear();
    }
}
