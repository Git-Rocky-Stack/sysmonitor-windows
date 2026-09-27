using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.Core.Models;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// A bare LED, 7 across (System-X styles.css :1792-1828). Only ever the companion of a word or a labelled row:
/// alone it would make colour the only carrier of meaning. So a screen reader passes over it unless the page
/// names it, when it is read as text, as System-X's is a status only when it has a label.
/// <para>
/// A warn dot blinks at 1 Hz like a warn lamp, and holds lit when Windows' animation effects are off.
/// <see cref="IsPulsing"/> breathes it on a 2 second cycle, which is reserved for the ON AIR light.
/// </para>
/// </summary>
public sealed class LedDot : Control
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(
        nameof(State), typeof(LampState), typeof(LedDot), new PropertyMetadata(LampState.Off, OnLookChanged));

    public static readonly DependencyProperty IsPulsingProperty = DependencyProperty.Register(
        nameof(IsPulsing), typeof(bool), typeof(LedDot), new PropertyMetadata(false, OnLookChanged));

    public LedDot()
    {
        IsTabStop = false;
        Loaded += (_, _) => UpdateStates();
    }

    public LampState State
    {
        get => (LampState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public bool IsPulsing
    {
        get => (bool)GetValue(IsPulsingProperty);
        set => SetValue(IsPulsingProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateStates();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new LedDotAutomationPeer(this);

    private static void OnLookChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((LedDot)owner).UpdateStates();

    private void UpdateStates()
    {
        var still = Motion.IsReduced;
        VisualStateManager.GoToState(this, State == LampState.Warn && still ? "WarnSteady" : State.ToString(), false);
        VisualStateManager.GoToState(this, IsPulsing && !still ? "Pulsing" : "Steady", false);
    }

    private sealed class LedDotAutomationPeer(LedDot owner) : FrameworkElementAutomationPeer(owner)
    {
        private bool IsNamed => !string.IsNullOrEmpty(AutomationProperties.GetName(Owner));

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Text;

        protected override string GetClassNameCore() => nameof(LedDot);

        protected override bool IsControlElementCore() => IsNamed;

        protected override bool IsContentElementCore() => IsNamed;
    }
}
