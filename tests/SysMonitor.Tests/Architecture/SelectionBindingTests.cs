using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A list control shows and selects its items through compiled bindings, never through a property named in a
/// string.
/// <para>
/// <c>SelectedValuePath</c> and <c>DisplayMemberPath</c> look the property up by name at run time, and the plain
/// option classes these pages bind to do not answer that lookup. Drive Wiper's Wipe Method and Scheduled Clean's
/// Schedule Type both sat empty over a value the view model held (3 passes, Weekly), and nothing reported it: a
/// failed path lookup is silent. The boxes now bind <c>SelectedItem</c> to the option object and draw each item
/// with an <c>x:DataType</c> template, which the compiler checks.
/// </para>
/// </summary>
public class SelectionBindingTests
{
    private static readonly Regex PathByName = new(@"\b(SelectedValuePath|DisplayMemberPath)\s*=", RegexOptions.Compiled);

    [Fact]
    public void NoListControlSelectsOrShowsItsItemsByAPropertyNamedInAString()
    {
        var offenders = RepoSource.FilesUnder("src/SysMonitor.App", "*.xaml")
            .SelectMany(path => File.ReadAllLines(path)
                .Select((line, index) => (line, number: index + 1))
                .Where(entry => PathByName.IsMatch(entry.line))
                .Select(entry => $"{RepoSource.Relative(path)}:{entry.number}"))
            .ToList();

        offenders.Should().BeEmpty(
            "a property path in a string is resolved by reflection at run time and fails silently on a plain class; " +
            "bind SelectedItem to the item and give the items an x:DataType template");
    }
}
