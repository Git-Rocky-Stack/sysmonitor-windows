using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.Core.Helpers;

namespace SysMonitor.App.Controls.Instruments;

/// <summary>
/// The panel that opens every page (System-X src/components/console/ViewHeader.tsx): a faceplate whose stripe
/// carries the module's kicker, <c>MOD - {Module} - {Index}</c>, its serial and the page's lamps, and whose body
/// carries the placard - the page's title in Archivo capitals - over a line of description, with the controls
/// that act on the module at the right. The first thing on screen is a panel, not a document.
/// <para>
/// <see cref="Title"/> is written as the page names itself and shown in capitals, so the source string stays the
/// one the documentation quotes. <see cref="Status"/> and <see cref="Actions"/> take whatever the page gives them,
/// usually a row of lamps and a row of caps.
/// </para>
/// </summary>
public sealed class ViewHeader : Control
{
    public static readonly DependencyProperty ModuleProperty = DependencyProperty.Register(
        nameof(Module), typeof(string), typeof(ViewHeader), new PropertyMetadata(string.Empty, OnNamingChanged));

    public static readonly DependencyProperty IndexProperty = DependencyProperty.Register(
        nameof(Index), typeof(string), typeof(ViewHeader), new PropertyMetadata("01", OnNamingChanged));

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(ViewHeader), new PropertyMetadata(string.Empty, OnNamingChanged));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(ViewHeader),
        new PropertyMetadata(string.Empty, OnDescriptionChanged));

    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(object), typeof(ViewHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions), typeof(object), typeof(ViewHeader), new PropertyMetadata(null));

    public static readonly DependencyProperty KickerTextProperty = DependencyProperty.Register(
        nameof(KickerText), typeof(string), typeof(ViewHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SerialTextProperty = DependencyProperty.Register(
        nameof(SerialText), typeof(string), typeof(ViewHeader), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty TitleTextProperty = DependencyProperty.Register(
        nameof(TitleText), typeof(string), typeof(ViewHeader), new PropertyMetadata(string.Empty));

    public ViewHeader()
    {
        IsTabStop = false;
        Rename();
    }

    /// <summary>The module's name in capitals, as the rail knows it: DASH, CPU, PDF EDIT.</summary>
    public string Module
    {
        get => (string)GetValue(ModuleProperty);
        set => SetValue(ModuleProperty, value);
    }

    /// <summary>The module's two-digit place in the navigation rail.</summary>
    public string Index
    {
        get => (string)GetValue(IndexProperty);
        set => SetValue(IndexProperty, value);
    }

    /// <summary>The page's title as written; the placard shows it in capitals.</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>One line on what the page is looking at.</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>The stripe's right-hand slot: the page's lamps and small readouts.</summary>
    public object? Status
    {
        get => GetValue(StatusProperty);
        set => SetValue(StatusProperty, value);
    }

    /// <summary>The controls that act on the module, at the right of the body.</summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    /// <summary>The stripe's kicker: "MOD - {Module} - {Index}".</summary>
    public string KickerText
    {
        get => (string)GetValue(KickerTextProperty);
        private set => SetValue(KickerTextProperty, value);
    }

    /// <summary>
    /// The module's serial (<see cref="PanelSerial"/>), the same one System-X stamps on a module of that name.
    /// </summary>
    public string SerialText
    {
        get => (string)GetValue(SerialTextProperty);
        private set => SetValue(SerialTextProperty, value);
    }

    /// <summary>The title in capitals, as the placard shows it.</summary>
    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        private set => SetValue(TitleTextProperty, value);
    }

    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        UpdateDescription();
    }

    private static void OnNamingChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((ViewHeader)owner).Rename();

    private static void OnDescriptionChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((ViewHeader)owner).UpdateDescription();

    private void Rename()
    {
        var module = Module ?? string.Empty;
        KickerText = $"MOD - {module} - {Index}";
        SerialText = PanelSerial.For(module);
        TitleText = (Title ?? string.Empty).ToUpperInvariant();
    }

    private void UpdateDescription() =>
        VisualStateManager.GoToState(this,
            string.IsNullOrEmpty(Description) ? "NoDescription" : "HasDescription", false);
}
