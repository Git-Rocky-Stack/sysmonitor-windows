using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// Keeps a <see cref="CastShadow"/> on a control's shadow host for as long as the control is loaded and has its
/// template, and draws whatever layers the control says it casts now. A control calls <see cref="Refresh"/> when
/// something its layers depend on changes: a colour from the palette, a state, the pointer.
/// </summary>
internal sealed class ShadowPart
{
    private readonly Control _owner;
    private readonly float _radius;
    private readonly Func<IEnumerable<ShadowLayer>> _layers;
    private FrameworkElement? _host;
    private CastShadow? _shadow;

    public ShadowPart(Control owner, float radius, Func<IEnumerable<ShadowLayer>> layers)
    {
        _owner = owner;
        _radius = radius;
        _layers = layers;
        owner.Loaded += (_, _) => Refresh();
        owner.Unloaded += (_, _) => Release();
    }

    /// <summary>The layers drawn now, for the smoke run to count.</summary>
    public int LayerCount => _shadow?.LayerCount ?? 0;

    /// <summary>Moves the shadow to a new template's host, or drops it when the template has none.</summary>
    public void Attach(FrameworkElement? host)
    {
        Release();
        _host = host;
        Refresh();
    }

    public void Refresh()
    {
        if (_host is null || !_owner.IsLoaded)
            return;

        _shadow ??= new CastShadow(_host, _radius);
        _shadow.Show(_layers());
    }

    private void Release()
    {
        _shadow?.Dispose();
        _shadow = null;
    }
}
