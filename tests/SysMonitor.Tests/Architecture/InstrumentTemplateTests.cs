using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A ContentPresenter in a console template presents the control's own Content, and nothing else: a second slot,
/// such as a faceplate's stripe, is a ContentControl.
/// <para>
/// WinUI binds a ContentPresenter in a ContentControl's template to the control's Content whenever the presenter's
/// own Content is still unset (CContentPresenter::ApplyTemplate), and a TemplateBinding to a property the app
/// registers is not recorded as set (CControl::SubscribeToPropertyChanges returns S_FALSE for it, so
/// SetTemplateBinding never notes it). The faceplate's stripe slot was a presenter bound to StripeRight: while the
/// slot was empty it took the faceplate's body as well, and a body that is an element, given a second parent, ended
/// the process from inside layout ("Value does not fall within the expected range"). Text for a body would have
/// been drawn twice instead. A ContentControl has no such default, so it shows only what it is given.
/// </para>
/// </summary>
public class InstrumentTemplateTests
{
    private const string ConsoleFolder = "src/SysMonitor.App/Styles/Console";

    /// <summary>
    /// Controls the hazard above cannot reach, because WinUI's fallback is to the templated parent's own
    /// Content and a Control that is not a ContentControl has none. ToggleSwitch is the case: Header, OnContent
    /// and OffContent are all it has, WinUI's own template binds presenters to exactly those three, and the
    /// substitute this rule prescribes elsewhere - a ContentControl - is not what the control's code looks for
    /// when it shows and hides HeaderContentPresenter, so it would cost every switch its header.
    /// <para>
    /// The exemption is checked, not taken on trust: TheExemptedControlsReallyHaveNoContentOfTheirOwn asks the
    /// framework itself, so listing a ContentControl here fails rather than quietly reopening the crash.
    /// </para>
    /// </summary>
    private static readonly string[] ControlsWithNoContentOfTheirOwn = ["ToggleSwitch"];

    /// <summary>The one thing a template's presenter may show: the control's own Content.</summary>
    private static readonly Regex OwnContent = new(@"^\{TemplateBinding\s+Content\}$", RegexOptions.Compiled);

    [Fact]
    public void TheRuleCatchesASecondPresenterAndAcceptsAContentControl()
    {
        var dictionary = XDocument.Parse("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Style x:Key="PanelStyle" TargetType="ContentControl">
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ContentControl">
                                <Grid>
                                    <ContentPresenter Content="{TemplateBinding Content}"/>
                                    <ContentPresenter/>
                                    <ContentControl Content="{TemplateBinding Tag}"/>
                                    <ContentPresenter Content="{TemplateBinding Tag}"/>
                                </Grid>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </ResourceDictionary>
            """, LoadOptions.SetLineInfo);

        PresentersOfSomethingElse(dictionary).Should().Equal(
            ["11 {TemplateBinding Tag}"],
            "a presenter of anything but the control's own Content is caught, and a ContentControl slot is not");
    }

    /// <summary>
    /// The exemption is narrow: it turns the rule off for one template, not for the file it sits in, and not
    /// for a presenter of something else in any other template. Without this, adding a control to the list
    /// could quietly stop the rule reading its neighbours.
    /// </summary>
    [Fact]
    public void TheExemptionReachesOnlyTheTemplateItIsFor()
    {
        var dictionary = XDocument.Parse("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Style TargetType="ToggleSwitch">
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ToggleSwitch">
                                <ContentPresenter Content="{TemplateBinding Header}"/>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
                <Style TargetType="ContentControl">
                    <Setter Property="Template">
                        <Setter.Value>
                            <ControlTemplate TargetType="ContentControl">
                                <ContentPresenter Content="{TemplateBinding Tag}"/>
                            </ControlTemplate>
                        </Setter.Value>
                    </Setter>
                </Style>
            </ResourceDictionary>
            """, LoadOptions.SetLineInfo);

        PresentersOfSomethingElse(dictionary).Should().Equal(
            ["16 {TemplateBinding Tag}"],
            "the switch's header presenter is exempt and the ContentControl's Tag presenter in the very same " +
            "dictionary is still caught");
    }

    [Fact]
    public void EveryPresenterInAConsoleTemplatePresentsTheControlsOwnContent()
    {
        var files = RepoSource.FilesUnder(ConsoleFolder, "*.xaml");
        files.Should().Contain(file => file.EndsWith("Instruments.xaml", StringComparison.Ordinal),
            "this test is worthless if it cannot find the instruments' templates");

        var offenders = files
            .SelectMany(file => PresentersOfSomethingElse(XDocument.Load(file, LoadOptions.SetLineInfo))
                .Select(presenter => $"{RepoSource.Relative(file)}:{presenter}"))
            .ToList();

        offenders.Should().BeEmpty(
            "a presenter bound to anything but Content is also bound to Content by WinUI while that thing is unset, " +
            "and then the control's content has two parents; a second slot is a ContentControl");
    }

    /// <summary>"line content" for each ContentPresenter in a template that is told to show something other than Content.</summary>
    private static IEnumerable<string> PresentersOfSomethingElse(XDocument document) =>
        document.Descendants()
            .Where(element => element.Name.LocalName == "ControlTemplate")
            .Where(template => !ControlsWithNoContentOfTheirOwn.Contains(
                template.Attribute("TargetType")?.Value, StringComparer.Ordinal))
            .SelectMany(template => template.Descendants())
            .Where(element => element.Name.LocalName == "ContentPresenter")
            .Select(presenter => (Presenter: presenter, Content: presenter.Attribute("Content")?.Value))
            .Where(entry => entry.Content is not null && !OwnContent.IsMatch(entry.Content))
            .Select(entry => $"{((IXmlLineInfo)entry.Presenter).LineNumber} {entry.Content}");
}
