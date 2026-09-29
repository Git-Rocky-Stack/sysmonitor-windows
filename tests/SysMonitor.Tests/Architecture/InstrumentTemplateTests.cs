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
            .SelectMany(template => template.Descendants())
            .Where(element => element.Name.LocalName == "ContentPresenter")
            .Select(presenter => (Presenter: presenter, Content: presenter.Attribute("Content")?.Value))
            .Where(entry => entry.Content is not null && !OwnContent.IsMatch(entry.Content))
            .Select(entry => $"{((IXmlLineInfo)entry.Presenter).LineNumber} {entry.Content}");
}
