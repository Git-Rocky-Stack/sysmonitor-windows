using System.Windows.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.Core.Helpers;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// The panel a page shows when it has nothing to report: a standby faceplate rather than an illustration
/// (System-X src/components/EmptyState.tsx). STBY is a real console state, so it gets a real lamp, in the stripe
/// where every panel carries its state, and the body is one centred column: an icon in a small well, a title, a
/// line of description and, when there is something to do about it, one action.
/// <para>
/// The kicker is expected, conventionally <c>STBY - MODULE - 01A</c>: a standby panel is the whole content of a
/// page with nothing to show, and one without a stripe reads as an unlabelled plate. <see cref="Glyph"/> is a
/// character of Windows' symbol font, as the app's other icons are.
/// </para>
/// </summary>
public sealed class StandbyPanel : Control
{
    public static readonly DependencyProperty KickerProperty = DependencyProperty.Register(
        nameof(Kicker), typeof(string), typeof(StandbyPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(StandbyPanel), new PropertyMetadata(string.Empty, OnTitleChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(StandbyPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(StandbyPanel), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ActionTextProperty = DependencyProperty.Register(
        nameof(ActionText), typeof(string), typeof(StandbyPanel), new PropertyMetadata(string.Empty, OnActionChanged));

    public static readonly DependencyProperty ActionCommandProperty = DependencyProperty.Register(
        nameof(ActionCommand), typeof(ICommand), typeof(StandbyPanel), new PropertyMetadata(null, OnActionChanged));

    public static readonly DependencyProperty SerialTextProperty = DependencyProperty.Register(
        nameof(SerialText), typeof(string), typeof(StandbyPanel), new PropertyMetadata(PanelSerial.For(string.Empty)));

    public StandbyPanel()
    {
        IsTabStop = false;
    }

    /// <summary>The stripe's kicker, conventionally <c>STBY - MODULE - 01A</c>.</summary>
    public string Kicker
    {
        get => (string)GetValue(KickerProperty);
        set => SetValue(KickerProperty, value);
    }

    /// <summary>What there is nothing of, as a title. It also names the panel's serial.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Why, or what would change it, in a sentence.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>
    /// The icon in the well: a character of Windows' symbol font, written <c>&amp;#xE721;</c> for a search.
    /// </summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>The action's word; with <see cref="ActionCommand"/> it puts the action under the description.</summary>
    public string ActionText
    {
        get => (string)GetValue(ActionTextProperty);
        set => SetValue(ActionTextProperty, value);
    }

    public ICommand? ActionCommand
    {
        get => (ICommand?)GetValue(ActionCommandProperty);
        set => SetValue(ActionCommandProperty, value);
    }

    /// <summary>The panel's serial, from its title as System-X's is.</summary>
    public string SerialText
    {
        get => (string)GetValue(SerialTextProperty);
        private set => SetValue(SerialTextProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateAction();
    }

    private static void OnTitleChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((StandbyPanel)owner).SerialText = PanelSerial.For((string?)args.NewValue ?? string.Empty);

    private static void OnActionChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((StandbyPanel)owner).UpdateAction();

    private void UpdateAction() =>
        VisualStateManager.GoToState(this,
            ActionCommand is null || string.IsNullOrEmpty(ActionText) ? "NoAction" : "HasAction", false);
}
