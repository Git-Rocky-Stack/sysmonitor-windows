using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SysMonitor.Core.Models;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// Paints an element in the colour of a state. A status on the console carries a word and a
/// <see cref="LampState"/>, never a colour of its own, and the page says which part of an element the state
/// colours:
/// <code>
/// &lt;TextBlock Text="{x:Bind Status}" instruments:StateBrush.Foreground="{x:Bind State}"/&gt;
/// &lt;Border instruments:StateBrush.Background="{x:Bind State}"&gt;
/// </code>
/// <para>
/// Each part takes the palette's colour for that use. A word is the state colour (styles.css --state-go and its
/// kin, :631-635), which Day Shift darkens so it stays readable on silver. A fill - a badge, a dot, a bar - is the
/// rail colour (:1161-1165), darkened the same way. A wash behind something is the state's soft tint. Off is
/// silver mute as a word, anthracite as a fill, and no wash at all. Armed is ArmedLit as a word and armed red as
/// a fill.
/// </para>
/// <para>
/// The brush comes from <see cref="ConsolePalette"/> for the shift the element is shown in, and is looked up
/// again when that shift may have changed - when the element loads and when its theme changes - so a state
/// follows the shift as a {ThemeResource} would.
/// </para>
/// </summary>
public static class StateBrush
{
    /// <summary>The state colour of a word or a glyph: TextBlock, Control, ContentPresenter, IconElement.</summary>
    public static readonly DependencyProperty ForegroundProperty = Register("Foreground");

    /// <summary>A state's solid fill behind something: Border, Panel, Control, ContentPresenter.</summary>
    public static readonly DependencyProperty BackgroundProperty = Register("Background");

    /// <summary>A state's soft wash behind something, or nothing when the state is Off.</summary>
    public static readonly DependencyProperty WashProperty = Register("Wash");

    /// <summary>A state's edge: Border, Control.</summary>
    public static readonly DependencyProperty BorderBrushProperty = Register("BorderBrush");

    /// <summary>A shape filled with a state: a dot, a segment.</summary>
    public static readonly DependencyProperty FillProperty = Register("Fill");

    /// <summary>A shape stroked with a state: a ring, a line.</summary>
    public static readonly DependencyProperty StrokeProperty = Register("Stroke");

    /// <summary>The element's watcher, kept for its life, which looks again when the shift changes.</summary>
    private static readonly DependencyProperty WatcherProperty = DependencyProperty.RegisterAttached(
        "Watcher", typeof(ShiftWatcher), typeof(StateBrush), new PropertyMetadata(null));

    // The values are objects, not LampStates, so that the first state set is always a change - Off included,
    // which as a default would never call back - and so a binding that has not delivered yet paints nothing.
    public static void SetForeground(DependencyObject element, object? value) => element.SetValue(ForegroundProperty, value);
    public static object? GetForeground(DependencyObject element) => element.GetValue(ForegroundProperty);
    public static void SetBackground(DependencyObject element, object? value) => element.SetValue(BackgroundProperty, value);
    public static object? GetBackground(DependencyObject element) => element.GetValue(BackgroundProperty);
    public static void SetWash(DependencyObject element, object? value) => element.SetValue(WashProperty, value);
    public static object? GetWash(DependencyObject element) => element.GetValue(WashProperty);
    public static void SetBorderBrush(DependencyObject element, object? value) => element.SetValue(BorderBrushProperty, value);
    public static object? GetBorderBrush(DependencyObject element) => element.GetValue(BorderBrushProperty);
    public static void SetFill(DependencyObject element, object? value) => element.SetValue(FillProperty, value);
    public static object? GetFill(DependencyObject element) => element.GetValue(FillProperty);
    public static void SetStroke(DependencyObject element, object? value) => element.SetValue(StrokeProperty, value);
    public static object? GetStroke(DependencyObject element) => element.GetValue(StrokeProperty);

    /// <summary>The palette key a state takes as a word.</summary>
    internal static string WordKey(LampState state) => state switch
    {
        LampState.Go => "StateGoBrush",
        LampState.Hold => "StateHoldBrush",
        LampState.Warn => "StateWarnBrush",
        LampState.NoGo => "StateNoGoBrush",
        LampState.Exec => "StateExecBrush",
        LampState.Armed => "ArmedLitBrush",
        _ => "SilverMuteBrush",
    };

    /// <summary>The palette key a state takes as a solid fill, edge or stroke.</summary>
    internal static string FillKey(LampState state) => state switch
    {
        LampState.Go => "RailGoBrush",
        LampState.Hold => "RailHoldBrush",
        LampState.Warn => "RailWarnBrush",
        LampState.NoGo => "RailNoGoBrush",
        LampState.Exec => "RailExecBrush",
        LampState.Armed => "ArmedBrush",
        _ => "AnthraciteBrush",
    };

    /// <summary>The palette key a state takes as a wash, or none for Off.</summary>
    internal static string? WashKey(LampState state) => state switch
    {
        LampState.Go => "GoSoftBrush",
        LampState.Hold => "HoldSoftBrush",
        LampState.Warn => "WarnSoftBrush",
        LampState.NoGo => "NoGoSoftBrush",
        LampState.Exec => "ScopeSoftBrush",
        LampState.Armed => "ArmedSoftBrush",
        _ => null,
    };

    private static DependencyProperty Register(string name) => DependencyProperty.RegisterAttached(
        name, typeof(object), typeof(StateBrush), new PropertyMetadata(null, OnStateChanged));

    private static void OnStateChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is not FrameworkElement element)
            return;

        if (element.GetValue(WatcherProperty) is null)
            element.SetValue(WatcherProperty, new ShiftWatcher(element));

        Paint(element);
    }

    /// <summary>Paints every part of the element that has a state, in the shift it is shown in now.</summary>
    internal static void Paint(FrameworkElement element)
    {
        if (element.GetValue(ForegroundProperty) is LampState word)
            SetForegroundOf(element, ConsolePalette.BrushFor(element, WordKey(word)));

        if (element.GetValue(BackgroundProperty) is LampState fill)
            SetBackgroundOf(element, ConsolePalette.BrushFor(element, FillKey(fill)));

        if (element.GetValue(WashProperty) is LampState wash)
            SetBackgroundOf(element, WashKey(wash) is { } key
                ? ConsolePalette.BrushFor(element, key)
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent));

        if (element.GetValue(BorderBrushProperty) is LampState edge)
        {
            var brush = ConsolePalette.BrushFor(element, FillKey(edge));
            if (element is Border border) border.BorderBrush = brush;
            else if (element is Control control) control.BorderBrush = brush;
        }

        if (element is Shape shape)
        {
            if (element.GetValue(FillProperty) is LampState shapeFill)
                shape.Fill = ConsolePalette.BrushFor(element, FillKey(shapeFill));

            if (element.GetValue(StrokeProperty) is LampState stroke)
                shape.Stroke = ConsolePalette.BrushFor(element, FillKey(stroke));
        }
    }

    private static void SetForegroundOf(FrameworkElement element, Brush? brush)
    {
        switch (element)
        {
            case TextBlock text: text.Foreground = brush; break;
            case Control control: control.Foreground = brush; break;
            case ContentPresenter presenter: presenter.Foreground = brush; break;
            case IconElement icon: icon.Foreground = brush; break;
        }
    }

    private static void SetBackgroundOf(FrameworkElement element, Brush? brush)
    {
        switch (element)
        {
            case Border border: border.Background = brush; break;
            case Panel panel: panel.Background = brush; break;
            case Control control: control.Background = brush; break;
            case ContentPresenter presenter: presenter.Background = brush; break;
        }
    }

    /// <summary>
    /// Paints the element again when the shift it is shown in may have changed: when it loads, which is when it
    /// first has the theme of the page it is on, and when that theme changes. Windows' High Contrast is picked up
    /// at the same moments (ConsolePalette says why it cannot be listened for).
    /// </summary>
    private sealed class ShiftWatcher
    {
        public ShiftWatcher(FrameworkElement element)
        {
            element.ActualThemeChanged += (sender, _) => Paint(sender);
            element.Loaded += (sender, _) => Paint((FrameworkElement)sender);
        }
    }
}
