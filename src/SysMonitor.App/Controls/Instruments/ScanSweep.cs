using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// The busy sweep: a band of armed glow travelling down a dark well, System-X's <c>.scanline</c> (styles.css
/// :2517-2535, keyframes <c>scan</c> :489). <see cref="BusyWell"/> and <see cref="BusyPanel"/> each drive one
/// over two parts of their template: the area the sweep crosses, and the band.
/// <para>
/// The numbers are System-X's, which are proportions of the well: the band is 30% of the area's height and starts
/// 40% above it, and it travels from 120% of its own height up to 220% down, over 2.4 seconds on CSS's
/// ease-in-out curve, forever. The area clips it, as <c>overflow: hidden</c> does. They are recomputed whenever
/// the area changes size. With Windows' animation effects off the band is not shown at all, where a frozen band
/// would read as a stuck display.
/// </para>
/// </summary>
internal sealed class ScanSweep
{
    private static readonly TimeSpan Period = TimeSpan.FromSeconds(2.4);

    private readonly FrameworkElement _area;
    private readonly FrameworkElement _band;
    private readonly TranslateTransform _travel = new();
    private Storyboard? _storyboard;
    private bool _running;

    public ScanSweep(FrameworkElement area, FrameworkElement band)
    {
        _area = area;
        _band = band;
        _band.RenderTransform = _travel;
        _band.VerticalAlignment = VerticalAlignment.Top;
        _band.IsHitTestVisible = false;
        _area.SizeChanged += OnAreaSizeChanged;
    }

    /// <summary>Starts the sweep, or restarts it from the top; the band shows once the area has a height.</summary>
    public void Start()
    {
        _running = true;
        Restart();
    }

    public void Stop()
    {
        _running = false;
        _storyboard?.Stop();
        _storyboard = null;
    }

    /// <summary>Stops the sweep and lets go of the template it was given, for a template being replaced.</summary>
    public void Detach()
    {
        Stop();
        _area.SizeChanged -= OnAreaSizeChanged;
    }

    private void OnAreaSizeChanged(object sender, SizeChangedEventArgs e)
    {
        _area.Clip = new RectangleGeometry { Rect = new Rect(0, 0, e.NewSize.Width, e.NewSize.Height) };
        if (_running)
            Restart();
    }

    private void Restart()
    {
        _storyboard?.Stop();
        _storyboard = null;

        var height = _area.ActualHeight;
        if (Motion.IsReduced || height <= 0)
        {
            _band.Visibility = Visibility.Collapsed;
            return;
        }

        var band = 0.3 * height;
        _band.Height = band;
        _band.Margin = new Thickness(0, -0.4 * height, 0, 0);
        _band.Visibility = Visibility.Visible;

        // CSS's ease-in-out is cubic-bezier(0.42, 0, 0.58, 1): a key spline gives the same curve exactly.
        var travel = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        travel.KeyFrames.Add(new DiscreteDoubleKeyFrame { KeyTime = KeyTime.FromTimeSpan(TimeSpan.Zero), Value = -1.2 * band });
        travel.KeyFrames.Add(new SplineDoubleKeyFrame
        {
            KeyTime = KeyTime.FromTimeSpan(Period),
            Value = 2.2 * band,
            KeySpline = new KeySpline { ControlPoint1 = new Point(0.42, 0), ControlPoint2 = new Point(0.58, 1) },
        });
        Storyboard.SetTarget(travel, _travel);
        Storyboard.SetTargetProperty(travel, nameof(TranslateTransform.Y));

        _storyboard = new Storyboard();
        _storyboard.Children.Add(travel);
        _storyboard.Begin();
    }
}
