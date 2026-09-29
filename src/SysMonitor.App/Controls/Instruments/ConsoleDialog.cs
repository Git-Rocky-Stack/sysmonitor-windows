using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// Builds the application's dialogs, so each one opens where it should, looking the way it should.
/// <para>
/// A <see cref="ContentDialog"/> made in code needs three things a dialog declared in XAML gets for free. It
/// has no <see cref="UIElement.XamlRoot"/> until it is given one, and cannot open without it. It does not pick
/// up the implicit dialog style, so it falls back to the framework's older template unless
/// <c>DefaultContentDialogStyle</c> is set on it by hand. And it opens in the popup layer, outside the page, so
/// it wears the page's theme only if it is told to.
/// </para>
/// </summary>
internal static class ConsoleDialog
{
    /// <summary>A dialog that opens over <paramref name="owner"/>'s window, in the theme it is showing.</summary>
    public static ContentDialog Create(FrameworkElement owner)
    {
        return new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            RequestedTheme = owner.ActualTheme,

            // Defined by XamlControlsResources, which App.xaml merges first, so it is always there.
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
        };
    }

    /// <summary>
    /// Asks before something is done that cannot simply be undone. Cancel is the default button, so Enter or
    /// Escape does nothing, and only an explicit press of <paramref name="confirmText"/> returns true.
    /// </summary>
    public static async Task<bool> ConfirmAsync(FrameworkElement owner, string title, string message,
        string confirmText)
    {
        var dialog = Create(owner);
        dialog.Title = title;
        dialog.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        dialog.PrimaryButtonText = confirmText;
        dialog.CloseButtonText = "Cancel";
        dialog.DefaultButton = ContentDialogButton.Close;

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
