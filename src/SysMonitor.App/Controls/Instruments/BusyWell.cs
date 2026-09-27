using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// A region at work inside a panel that is already on screen: a dark well with the busy sweep passing down it and
/// one line saying what the machine is doing (System-X src/components/BusyState.tsx, BusyWell).
/// <para>
/// It stands in for the spinning ring, which turned at the same speed whatever the work was doing and so said
/// nothing but that it had started. A whole busy panel, <see cref="BusyPanel"/>, cannot sit inside a faceplate;
/// this is the same sweep on the same surface without the chrome. The line is a polite live region, so a screen
/// reader hears it when it changes.
/// </para>
/// </summary>
public sealed class BusyWell : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(BusyWell), new PropertyMetadata(string.Empty, OnTitleChanged));

    private ScanSweep? _sweep;
    private FrameworkElement? _line;

    public BusyWell()
    {
        IsTabStop = false;
        Loaded += (_, _) => _sweep?.Start();
        Unloaded += (_, _) => _sweep?.Stop();
    }

    /// <summary>What the machine is doing, in one line.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        _sweep?.Detach();
        _sweep = null;

        base.OnApplyTemplate();
        _line = GetTemplateChild("PART_Title") as FrameworkElement;
        if (GetTemplateChild("PART_ScanArea") is FrameworkElement area &&
            GetTemplateChild("PART_ScanBand") is FrameworkElement band)
        {
            _sweep = new ScanSweep(area, band);
            if (IsLoaded)
                _sweep.Start();
        }
    }

    /// <summary>
    /// Tells a screen reader the line changed. Queued, so the line's text has taken the new title by the time the
    /// reader asks for it.
    /// </summary>
    private static void OnTitleChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var well = (BusyWell)owner;
        if (!well.IsLoaded || well._line is not { } line ||
            !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            return;

        well.DispatcherQueue.TryEnqueue(() =>
        {
            var peer = FrameworkElementAutomationPeer.FromElement(line) ??
                       FrameworkElementAutomationPeer.CreatePeerForElement(line);
            peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        });
    }
}
