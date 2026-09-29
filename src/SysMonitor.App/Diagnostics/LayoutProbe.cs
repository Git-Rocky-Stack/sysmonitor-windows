using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace SysMonitor.App.Diagnostics;

/// <summary>
/// Lays out one element for the smoke run, and outlives it failing to. XAML treats a failure inside layout as
/// unrecoverable: it reports it to UnhandledException and then ends the process, even when the handler marks it
/// handled, so the first template that cannot be built takes the rest of the run with it, and a report that could
/// have named every failing instrument names one. Measured and arranged from here, the failure is caught on its way
/// back up, kept for the report, and the element is collapsed so the next layout pass leaves it alone.
/// </summary>
internal sealed class LayoutProbe : Panel
{
    public LayoutProbe(UIElement element) => Children.Add(element);

    /// <summary>The first failure measuring or arranging the element, or null while it lays out cleanly.</summary>
    public Exception? Failure { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var desired = new Size();
        foreach (var child in Children)
        {
            if (Survives(() => child.Measure(availableSize), child))
            {
                desired = new Size(
                    Math.Max(desired.Width, child.DesiredSize.Width),
                    Math.Max(desired.Height, child.DesiredSize.Height));
            }
        }

        return desired;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var child in Children)
            Survives(() => child.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height)), child);

        return finalSize;
    }

    private bool Survives(Action layout, UIElement child)
    {
        try
        {
            layout();
            return true;
        }
        catch (Exception ex)
        {
            Failure ??= ex;
            child.Visibility = Visibility.Collapsed;
            return false;
        }
    }
}
