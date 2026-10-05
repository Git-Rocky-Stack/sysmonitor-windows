using Microsoft.UI.Dispatching;
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
/// High Contrast does not change an element's <c>ActualTheme</c>, so <see cref="HighContrastChanged"/> says when it
/// is turned on or off, raised on the UI thread for whoever needs to look again.
/// </para>
/// </summary>
internal static class ConsolePalette
{
    private const string TokensSource = "Styles/Console/Tokens.xaml";

    private static readonly AccessibilitySettings Accessibility = new();
    private static readonly DispatcherQueue? Dispatcher = DispatcherQueue.GetForCurrentThread();
    private static ResourceDictionary? _tokens;

    static ConsolePalette()
    {
        // Windows raises this on a thread of its own; elements can only be touched from theirs.
        Accessibility.HighContrastChanged += (_, _) =>
            Dispatcher?.TryEnqueue(() => HighContrastChanged?.Invoke(null, EventArgs.Empty));
    }

    /// <summary>Raised on the UI thread when Windows turns High Contrast on or off.</summary>
    public static event EventHandler? HighContrastChanged;

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
        Accessibility.HighContrast ? "HighContrast" : theme == ElementTheme.Light ? "Light" : "Default";

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
