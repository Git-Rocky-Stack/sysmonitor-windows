using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// The one place code reads the console's palette. Styles/Console/Tokens.xaml writes every colour once per shift,
/// and XAML reaches the right shift with <c>{ThemeResource}</c>. Code cannot: <c>Application.Current.Resources</c>
/// answers for the application's theme, not the element's, so a brush code looked up there would be Night Ops'
/// on a Day Shift page. So this asks the palette's own theme dictionary for the shift the element is shown in -
/// Night Ops, Day Shift, or High Contrast whenever Windows has it on, as XAML's lookup does.
/// <para>
/// Whether High Contrast is on is asked of Windows at each lookup. Its change event is not: subscribing to
/// <c>AccessibilitySettings.HighContrastChanged</c> throws "element not found" in a desktop app, which has no
/// CoreWindow for it to report to - measured, it took the first page with a coloured status down with it. So an
/// element picks up a change of High Contrast when it is next painted: when it loads, when its theme changes, or
/// when its state does.
/// </para>
/// </summary>
internal static class ConsolePalette
{
    private const string TokensSource = "Styles/Console/Tokens.xaml";

    private static AccessibilitySettings? _accessibility;
    private static ResourceDictionary? _tokens;

    /// <summary>The palette's brush of this key for the shift <paramref name="element"/> is shown in.</summary>
    public static Brush? BrushFor(FrameworkElement element, string key)
    {
        var tokens = _tokens ??= Find(Application.Current.Resources);
        if (tokens is null || !tokens.ThemeDictionaries.TryGetValue(ShiftOf(element.ActualTheme), out var shift) ||
            shift is not ResourceDictionary brushes || !brushes.TryGetValue(key, out var brush))
            return null;

        return brush as Brush;
    }

    /// <summary>The palette's theme dictionary that answers for an element in this theme.</summary>
    private static string ShiftOf(ElementTheme theme) =>
        HighContrast() ? "HighContrast" : theme == ElementTheme.Light ? "Light" : "Default";

    /// <summary>Whether Windows has High Contrast on; a Windows that will not say is taken as not.</summary>
    private static bool HighContrast()
    {
        try
        {
            return (_accessibility ??= new AccessibilitySettings()).HighContrast;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static ResourceDictionary? Find(ResourceDictionary dictionary)
    {
        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (merged.Source?.OriginalString.EndsWith(TokensSource, StringComparison.OrdinalIgnoreCase) == true)
                return merged;

            if (Find(merged) is { } nested)
                return nested;
        }

        return null;
    }
}
