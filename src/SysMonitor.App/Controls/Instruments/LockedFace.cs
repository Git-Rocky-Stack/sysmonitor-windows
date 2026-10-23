using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// The face a cap takes while it is locked. CSS dims a locked control and half-desaturates it
/// (<c>opacity: .42; filter: saturate(.5)</c>, styles.css :2016-2023); WinUI has no filter, so a cap whose face is
/// not grey names the twin the palette works out for it, and wears it for as long as it is disabled:
/// <code>
/// &lt;Setter Property="instruments:LockedFace.Background" Value="{ThemeResource ArmedCapFaceLockedBrush}"/&gt;
/// </code>
/// The cap's template does the dimming, as it does for every cap; this only swaps the colours under it. Enabled
/// again, the cap's own face comes back from its style.
/// </summary>
public static class LockedFace
{
    public static readonly DependencyProperty BackgroundProperty = DependencyProperty.RegisterAttached(
        "Background", typeof(Brush), typeof(LockedFace), new PropertyMetadata(null, OnFaceChanged));

    public static readonly DependencyProperty BorderBrushProperty = DependencyProperty.RegisterAttached(
        "BorderBrush", typeof(Brush), typeof(LockedFace), new PropertyMetadata(null, OnFaceChanged));

    /// <summary>Set once a control is being watched, so a second face set on it does not watch it twice.</summary>
    private static readonly DependencyProperty IsWatchedProperty = DependencyProperty.RegisterAttached(
        "IsWatched", typeof(bool), typeof(LockedFace), new PropertyMetadata(false));

    public static void SetBackground(DependencyObject element, Brush? value) => element.SetValue(BackgroundProperty, value);
    public static Brush? GetBackground(DependencyObject element) => (Brush?)element.GetValue(BackgroundProperty);
    public static void SetBorderBrush(DependencyObject element, Brush? value) => element.SetValue(BorderBrushProperty, value);
    public static Brush? GetBorderBrush(DependencyObject element) => (Brush?)element.GetValue(BorderBrushProperty);

    private static void OnFaceChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        if (owner is not Control control)
            return;

        if (!(bool)control.GetValue(IsWatchedProperty))
        {
            control.SetValue(IsWatchedProperty, true);
            control.IsEnabledChanged += (_, _) => Apply(control);
        }

        Apply(control);
    }

    /// <summary>
    /// Locked, the twin is set on the control itself, over what its style gives it; unlocked, that is cleared and
    /// the style's face shows again. Only what this set is cleared - a control that is enabled and was never
    /// given a twin keeps whatever it has.
    /// </summary>
    private static void Apply(Control control)
    {
        Swap(control, Control.BackgroundProperty, GetBackground(control), control.IsEnabled);
        Swap(control, Control.BorderBrushProperty, GetBorderBrush(control), control.IsEnabled);
    }

    private static void Swap(Control control, DependencyProperty property, Brush? twin, bool enabled)
    {
        if (twin is null)
            return;

        if (!enabled)
            control.SetValue(property, twin);
        else if (ReferenceEquals(control.ReadLocalValue(property), twin))
            control.ClearValue(property);
    }
}
