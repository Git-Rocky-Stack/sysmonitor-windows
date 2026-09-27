using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>The colour an LCD's reading glows in: phosphor green unless the reading says otherwise.</summary>
public enum LcdTone
{
    Go,
    Amber,
    Red,
    Scope,
    NoGo,
}

/// <summary>
/// An LCD: a void-black window with a silkscreen caption over a Departure Mono reading that glows (System-X
/// src/components/console/Lcd.tsx; styles.css :1831-1892). If a number updates live, it belongs in one of
/// these; if a person reads it as prose, it belongs in Public Sans instead. It is dark in both shifts, which is
/// what keeps the phosphor identical across them.
/// <para>
/// <see cref="Value"/> is the reading as it is shown, already formatted, and <see cref="Unit"/> trails it,
/// quieter. A missing reading is <c>--</c>, as everywhere else in the app.
/// </para>
/// </summary>
public sealed class Lcd : Control
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(Lcd), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(Lcd), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(Lcd), new PropertyMetadata(string.Empty, OnLookChanged));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(LcdTone), typeof(Lcd), new PropertyMetadata(LcdTone.Go, OnLookChanged));

    public Lcd()
    {
        IsTabStop = false;
    }

    /// <summary>The caption, in capitals as it is shown.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public LcdTone Tone
    {
        get => (LcdTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateStates();
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new ReadingAutomationPeer(this, () => Spoken(Label, Value, Unit));

    private static void OnLookChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((Lcd)owner).UpdateStates();

    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, Tone.ToString(), false);
        VisualStateManager.GoToState(this, string.IsNullOrEmpty(Unit) ? "NoUnit" : "HasUnit", false);
    }

    /// <summary>A reading as a screen reader says it: "HEALTH SCORE, 92 %".</summary>
    internal static string Spoken(string label, string value, string unit) =>
        string.IsNullOrEmpty(unit) ? $"{label}, {value}" : $"{label}, {value} {unit}";
}

/// <summary>
/// One dense line in a shared well: the caption on the left, the reading on the right (System-X
/// src/components/console/Lcd.tsx, LcdRow). For a rail of many readings, where a boxed LCD for each would
/// waste the space.
/// </summary>
public sealed class LcdRow : Control
{
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(LcdRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(LcdRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(LcdTone), typeof(LcdRow), new PropertyMetadata(LcdTone.Go, OnToneChanged));

    public LcdRow()
    {
        IsTabStop = false;
    }

    /// <summary>The caption, in capitals as it is shown.</summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public LcdTone Tone
    {
        get => (LcdTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        VisualStateManager.GoToState(this, Tone.ToString(), false);
    }

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new ReadingAutomationPeer(this, () => Lcd.Spoken(Label, Value, string.Empty));

    private static void OnToneChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        VisualStateManager.GoToState((LcdRow)owner, ((LcdTone)args.NewValue).ToString(), false);
}

/// <summary>A reading is read as text, caption first, unless the page names it itself.</summary>
internal sealed class ReadingAutomationPeer(Control owner, Func<string> spoken) : FrameworkElementAutomationPeer(owner)
{
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

    protected override string GetClassNameCore() => Owner.GetType().Name;

    protected override string GetNameCore()
    {
        var named = AutomationProperties.GetName(Owner);
        return string.IsNullOrEmpty(named) ? spoken() : named;
    }
}
