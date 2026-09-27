using System.Xml.Linq;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// Every console dictionary (Styles/Console) resolves what it names on its own: from its own entries, the files it
/// merges, or WinUI's resources - never from a sibling that App.xaml happens to merge beside it.
/// <para>
/// A reference to a sibling resolves while App.xaml is being read, because the sibling was read first, so nothing
/// fails and nothing says the dictionary depends on where it is merged. Read anywhere else - on its own, or merged
/// ahead of that sibling - it finds nothing, and a template that names it fails when it is first built, which is
/// the first time a page shows it. Each console dictionary merges what it names instead, and this keeps it so.
/// </para>
/// </summary>
public class ConsoleDictionaryTests
{
    private const string ConsoleFolder = "src/SysMonitor.App/Styles/Console";


    [Fact]
    public void TheRuleCatchesASiblingAndAcceptsAMerge()
    {
        var fonts = XDocument.Parse("""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <FontFamily x:Key="MonoFace">ms-appx:///Assets/Fonts/Mono.ttf#Mono</FontFamily>
            </ResourceDictionary>
            """);
        (string, XDocument)? Open(string file, string source) => source.EndsWith("Fonts.xaml") ? ("Fonts.xaml", fonts) : null;

        const string Style = """<Style x:Key="LabelStyle" TargetType="TextBlock"><Setter Property="FontFamily" Value="{StaticResource MonoFace}"/></Style>""";
        var leaning = XDocument.Parse($"""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                {Style}
            </ResourceDictionary>
            """, LoadOptions.SetLineInfo);
        var merging = XDocument.Parse($"""
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <ResourceDictionary.MergedDictionaries>
                    <ResourceDictionary Source="ms-appx:///Styles/Fonts.xaml"/>
                </ResourceDictionary.MergedDictionaries>
                {Style}
            </ResourceDictionary>
            """, LoadOptions.SetLineInfo);
        var framework = new HashSet<string>(StringComparer.Ordinal) { "ControlCornerRadius" };

        ResourceKeyRule.Unresolved(leaning, "Typography.xaml", framework, Open).Select(reference => reference.Key)
            .Should().Equal(["MonoFace"], "a face that lives in a sibling is not there when this file is loaded on its own");
        ResourceKeyRule.Unresolved(merging, "Typography.xaml", framework, Open)
            .Should().BeEmpty("a face the file merges goes wherever the file goes");
    }

    [Fact]
    public void EveryConsoleDictionaryResolvesWhatItNamesOnItsOwn()
    {
        var files = RepoSource.FilesUnder(ConsoleFolder, "*.xaml");
        files.Should().HaveCountGreaterThan(4, "this test is worthless if it cannot find the dictionaries it judges");

        var unresolved = files
            .SelectMany(file => ResourceKeyRule.Unresolved(XDocument.Load(file, LoadOptions.SetLineInfo), file,
                    WinUIResources.Keys, ProjectDictionary.Open)
                .Select(reference => $"{RepoSource.Relative(file)}:{reference.Line} {reference.Key}"))
            .ToList();

        unresolved.Should().BeEmpty(
            "each console dictionary merges what it names: read on its own, or merged ahead of the sibling, a key " +
            "that lives in a sibling is not there");
    }
}
