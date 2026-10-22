namespace SysMonitor.App.ViewModels;

/// <summary>
/// Asks the person before something is done that cannot simply be undone, and answers whether they said yes.
/// <para>
/// A view model cannot open a dialog - it has no window - so its page hands it one: the page sets the view
/// model's <c>Confirm</c> to <c>ConsoleDialog.ConfirmAsync</c> over itself. A view model whose page never did is
/// taken to have been told no: an armed action that runs because nobody wired the question is exactly the
/// failure this exists to prevent.
/// </para>
/// </summary>
public delegate Task<bool> Confirmation(string title, string message, string confirmText);

internal static class ConfirmationExtensions
{
    /// <summary>The answer to <paramref name="confirm"/>, or no when there is nobody to ask.</summary>
    public static Task<bool> AskAsync(this Confirmation? confirm, string title, string message, string confirmText) =>
        confirm?.Invoke(title, message, confirmText) ?? Task.FromResult(false);
}
