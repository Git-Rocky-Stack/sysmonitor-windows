using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.Core.Models;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// An inline condition report: a fault, a caution or a note, as a small lamp and a line of prose (System-X
/// src/components/console/Banner.tsx; styles.css :2230-2283).
/// <para>
/// Status is a word, then a colour: the lamp keeps the banner legible to someone who cannot tell the LED hues
/// apart, and a fault reads NO-GO rather than borrowing the armed red that means live. <see cref="State"/> is
/// <see cref="LampState.NoGo"/> unless set, because most banners report a failure. A fault the user can dismiss is
/// still NO-GO, never Warn: Warn blinks, and a whole banner blinking is noise. The dismiss cap is what
/// acknowledges it, and the banner does not hide itself - the page decides what dismissing means.
/// </para>
/// <para>
/// A screen reader hears a fault at once and anything calmer in its turn, and hears it again whenever the state,
/// the word or the message changes, without focus moving to it.
/// </para>
/// </summary>
public sealed class Banner : ContentControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(LampState), typeof(Banner), new PropertyMetadata(LampState.NoGo, OnReportChanged));

    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(Banner), new PropertyMetadata(null, OnReportChanged));

    public static readonly DependencyProperty IsDismissibleProperty = DependencyProperty.Register(
        nameof(IsDismissible), typeof(bool), typeof(Banner), new PropertyMetadata(false, OnDismissChanged));

    public static readonly DependencyProperty DismissLabelProperty = DependencyProperty.Register(
        nameof(DismissLabel), typeof(string), typeof(Banner), new PropertyMetadata("Dismiss", OnDismissChanged));

    public static readonly DependencyProperty ShownCodeProperty = DependencyProperty.Register(
        nameof(ShownCode), typeof(string), typeof(Banner), new PropertyMetadata(string.Empty));

    private Button? _dismiss;

    public Banner()
    {
        UpdateReport();
    }

    /// <summary>Raised when the user presses the dismiss cap.</summary>
    public event EventHandler? Dismissed;

    public LampState State
    {
        get => (LampState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    /// <summary>The lamp's word, two to seven capitals so the lamp fits; the state's own word when not set.</summary>
    public string? Code
    {
        get => (string?)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    /// <summary>A dismiss cap at the end of the line. Leave it off for a condition the user cannot clear.</summary>
    public bool IsDismissible
    {
        get => (bool)GetValue(IsDismissibleProperty);
        set => SetValue(IsDismissibleProperty, value);
    }

    /// <summary>What a screen reader calls the dismiss cap. Name what it dismisses: "Dismiss scan error".</summary>
    public string DismissLabel
    {
        get => (string)GetValue(DismissLabelProperty);
        set => SetValue(DismissLabelProperty, value);
    }

    /// <summary>The word the lamp shows: <see cref="Code"/>, or the state's own.</summary>
    public string ShownCode
    {
        get => (string)GetValue(ShownCodeProperty);
        private set => SetValue(ShownCodeProperty, value);
    }

    /// <summary>The word each state wears when the page names none, as System-X's banners do.</summary>
    internal static string DefaultCode(LampState state) => state switch
    {
        LampState.Go => "OK",
        LampState.Hold => "HOLD",
        LampState.Warn => "CAUTION",
        LampState.NoGo => "NO-GO",
        LampState.Exec => "NOTE",
        LampState.Armed => "LIVE",
        _ => "INFO",
    };

    protected override void OnApplyTemplate()
    {
        if (_dismiss is not null)
            _dismiss.Click -= OnDismissClick;

        base.OnApplyTemplate();
        _dismiss = GetTemplateChild("PART_Dismiss") as Button;
        if (_dismiss is not null)
            _dismiss.Click += OnDismissClick;

        UpdateReport();
        UpdateDismiss();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        Announce();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new BannerAutomationPeer(this);

    private static void OnReportChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var banner = (Banner)owner;
        banner.UpdateReport();
        banner.Announce();
    }

    private static void OnDismissChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((Banner)owner).UpdateDismiss();

    private bool IsFault => State is LampState.NoGo or LampState.Warn;

    private void UpdateReport()
    {
        ShownCode = string.IsNullOrEmpty(Code) ? DefaultCode(State) : Code;
        AutomationProperties.SetLiveSetting(this, IsFault ? AutomationLiveSetting.Assertive : AutomationLiveSetting.Polite);
        VisualStateManager.GoToState(this, State.ToString(), false);
    }

    private void UpdateDismiss()
    {
        VisualStateManager.GoToState(this, IsDismissible ? "Dismissible" : "Standing", false);
        if (_dismiss is not null)
            AutomationProperties.SetName(_dismiss, DismissLabel);
    }

    /// <summary>Tells a screen reader the report changed, so it is heard without focus moving to it.</summary>
    private void Announce()
    {
        if (!IsLoaded || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged))
            return;

        var peer = FrameworkElementAutomationPeer.FromElement(this) ?? FrameworkElementAutomationPeer.CreatePeerForElement(this);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void OnDismissClick(object sender, RoutedEventArgs e) => Dismissed?.Invoke(this, EventArgs.Empty);

    /// <summary>The message as text, whether the page gave a string or a TextBlock.</summary>
    private string? MessageText => Content switch
    {
        string message => message,
        TextBlock block => block.Text,
        _ => null,
    };

    /// <summary>
    /// A group read as its lamp's word and its message, "NO-GO: 3 files could not be recycled", which is what a
    /// live region announces; the lamp, the message and the dismiss cap stay inside it for anyone moving through
    /// them.
    /// </summary>
    private sealed class BannerAutomationPeer(Banner owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(Banner);

        protected override string GetNameCore()
        {
            var banner = (Banner)Owner;
            var named = AutomationProperties.GetName(banner);
            if (!string.IsNullOrEmpty(named))
                return named;

            return string.IsNullOrEmpty(banner.MessageText) ? banner.ShownCode : $"{banner.ShownCode}: {banner.MessageText}";
        }
    }
}
