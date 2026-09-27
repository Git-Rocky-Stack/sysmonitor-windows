using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// Keeps a <see cref="Glow"/> round a lit part of a control's template for as long as the control is loaded, in
/// the colour the control gives for it now. The glow's colour comes from the palette through the template - the
/// brush a visual state gave the lit part - so the part watches that brush: a new state, a new shift and High
/// Contrast all arrive as a change of it, and each relights the glow.
/// </summary>
internal sealed class GlowPart
{
    private readonly Control _owner;
    private readonly float _blur;
    private readonly float _spread;
    private readonly Func<Color> _colour;
    private FrameworkElement? _host;
    private FrameworkElement? _source;
    private DependencyObject? _watched;
    private DependencyProperty? _brush;
    private long _token;
    private Glow? _glow;

    public GlowPart(Control owner, float blur, float spread, Func<Color> colour)
    {
        _owner = owner;
        _blur = blur;
        _spread = spread;
        _colour = colour;
        owner.Loaded += (_, _) => Refresh();
        owner.Unloaded += (_, _) => Release();
    }

    /// <summary>Whether the glow is drawn now, for the smoke run to check.</summary>
    public bool IsLit => _glow?.IsLit == true;

    /// <summary>
    /// Moves the glow to a new template: the host it is drawn on, the lit element whose outline glows, and the
    /// brush whose changes relight it.
    /// </summary>
    public void Attach(FrameworkElement? host, FrameworkElement? source, DependencyObject? watched,
        DependencyProperty? brush)
    {
        Release();
        Unwatch();
        _host = host;
        _source = source;
        _watched = watched;
        _brush = brush;
        if (_watched is not null && _brush is not null)
            _token = _watched.RegisterPropertyChangedCallback(_brush, (_, _) => Refresh());

        Refresh();
    }

    public void Refresh()
    {
        if (_host is null || _source is null || !_owner.IsLoaded)
            return;

        // Built once the control is in the tree, which is where the glow finds out whether it is in a list.
        _glow ??= new Glow(_host, _source, _blur, _spread);
        _glow.Light(_colour());
    }

    private void Release()
    {
        _glow?.Dispose();
        _glow = null;
    }

    private void Unwatch()
    {
        if (_watched is not null && _brush is not null)
            _watched.UnregisterPropertyChangedCallback(_brush, _token);

        _watched = null;
        _brush = null;
    }
}
