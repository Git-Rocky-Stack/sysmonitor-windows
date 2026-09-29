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
/// <para>
/// And its buttons are caps, which it also has to be told. WinUI's dialog style gives all three buttons
/// <c>DefaultButtonStyle</c> and then hands the accent style to whichever one is the default, from a visual
/// state (generic.xaml, DefaultButtonStates). Since the accent became armed red the emphasis that state
/// produces is only right when the default button is the one that does the thing - so every dialog here is
/// given its caps outright, and a confirmation has no default button at all.
/// </para>
/// </summary>
internal static class ConsoleDialog
{
    /// <summary>
    /// A dialog that opens over <paramref name="owner"/>'s window, in the theme it is showing, with plain caps
    /// on all three buttons. A caller that has a consequential button arms that one itself.
    /// </summary>
    public static ContentDialog Create(FrameworkElement owner)
    {
        var plain = Cap("ConsoleCapButtonStyle");

        return new ContentDialog
        {
            XamlRoot = owner.XamlRoot,
            RequestedTheme = owner.ActualTheme,

            // Defined by XamlControlsResources, which App.xaml merges first, so it is always there.
            Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,

            // Every button starts plain; nothing is emphasised until a caller says which.
            PrimaryButtonStyle = plain,
            SecondaryButtonStyle = plain,
            CloseButtonStyle = plain,
        };
    }

    /// <summary>
    /// Says something and waits to be dismissed. Nothing is decided, so nothing is armed and there is one
    /// button. <paramref name="content"/> is text or an element, as <see cref="ContentDialog.Content"/> takes it.
    /// </summary>
    public static async Task TellAsync(FrameworkElement owner, string title, object content, string closeText = "Close")
    {
        var dialog = Create(owner);
        dialog.Title = title;
        dialog.Content = content is string text ? new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap } : content;
        dialog.CloseButtonText = closeText;

        await dialog.ShowAsync();
    }

    /// <summary>
    /// Asks before something is done that cannot simply be undone. Nothing is the default button, so Enter arms
    /// nothing and Escape cancels, and only an explicit press of <paramref name="confirmText"/> returns true.
    /// </summary>
    public static async Task<bool> ConfirmAsync(FrameworkElement owner, string title, string message,
        string confirmText) =>
        await Confirm(owner, title, message, confirmText).ShowAsync() == ContentDialogResult.Primary;

    /// <summary>
    /// The confirmation <see cref="ConfirmAsync"/> shows, built but not yet opened, so the UI smoke run can open
    /// it and read which button WinUI actually drew in which style (UiSmokeRun.CheckDialogsAsync). That is the
    /// only place it can be read: the style a dialog's buttons end up with is settled by the framework's own
    /// visual states when the dialog opens, not by anything this method can be seen to set.
    /// <para>
    /// Which is why there is no default button. Cancel used to be the default, so that Enter armed nothing - but
    /// WinUI gives the default button the accent style from a visual state, and that state beats the style set
    /// here. With the accent armed red since Phase 1, Cancel came out armed red and the button that does the
    /// thing came out plain: the emphasis backwards on exactly the dialogs where it matters most. With no default
    /// button the state never runs, the caps below are what is drawn, and Enter still arms nothing.
    /// </para>
    /// </summary>
    public static ContentDialog Confirm(FrameworkElement owner, string title, string message, string confirmText)
    {
        var dialog = Create(owner);
        dialog.Title = title;
        dialog.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap };
        dialog.PrimaryButtonText = confirmText;
        dialog.CloseButtonText = "Cancel";

        // The consequential button is the armed cap; Cancel keeps the plain one Create gave it.
        dialog.PrimaryButtonStyle = Cap("ArmedCapButtonStyle");
        dialog.DefaultButton = ContentDialogButton.None;

        return dialog;
    }

    /// <summary>A cap from the console's controls dictionary, which App.xaml merges (Styles/Console/Controls.xaml).</summary>
    private static Style? Cap(string key) => Application.Current.Resources[key] as Style;
}
