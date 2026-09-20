using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A view model is built for one visit to a page and thrown away when the user leaves. The services it talks
/// to are singletons that live as long as the app, so anything a view model hands them - an event handler -
/// keeps that view model alive, and with it the page, its bindings, and every chart and image they hold. Each
/// visit then adds another copy.
/// <para>
/// These rules cannot be checked by running a view model: the app is WinUI and this project has no UI host.
/// They are checked by reading the source instead, which is enough, because the mistake is always visible
/// there: a subscription with no matching unsubscribe, or a page that never disposes what it created.
/// </para>
/// </summary>
public class ViewModelLifetimeTests
{
    private const string ViewModelFolder = "src/SysMonitor.App/ViewModels";
    private const string ViewFolder = "src/SysMonitor.App/Views";

    /// <summary>`_someService.SomeEvent += OnSomething;` - handing a handler to something the view model does not own.</summary>
    private static readonly Regex Subscription =
        new(@"^[ \t]*(?<target>_\w+)\.(?<event>\w+)\s*\+=\s*(?<handler>\w+);", RegexOptions.Multiline);

    private static readonly Regex DisposableViewModel =
        new(@"class\s+(?<name>\w+ViewModel)\s*:[^{\r\n]*\bIDisposable\b");

    private static readonly Regex DisposeBody =
        new(@"public\s+void\s+Dispose\(\)\s*\{(?<body>.*?)\r?\n {4}\}", RegexOptions.Singleline);

    [Fact]
    public void EveryViewModelUndoesTheSubscriptionsItMakes()
    {
        var leaks = new List<string>();

        foreach (var file in RepoSource.FilesUnder(ViewModelFolder))
        {
            var source = File.ReadAllText(file);

            foreach (Match match in Subscription.Matches(source))
            {
                var target = match.Groups["target"].Value;
                var name = match.Groups["event"].Value;
                var handler = match.Groups["handler"].Value;

                var unsubscribe = new Regex($@"{Regex.Escape(target)}\.{Regex.Escape(name)}\s*-=\s*{Regex.Escape(handler)};");
                if (!unsubscribe.IsMatch(source))
                    leaks.Add($"{RepoSource.Relative(file)}: {target}.{name} += {handler} is never undone");
            }
        }

        leaks.Should().BeEmpty(
            "a view model that stays subscribed to a singleton service is kept alive by it, along with the page bound to it");
    }

    [Fact]
    public void EveryDisposableViewModelIsDisposedByThePageThatBuildsIt()
    {
        var pages = RepoSource.FilesUnder(ViewFolder)
            .Where(file => file.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase))
            .Select(file => (Path: file, Source: File.ReadAllText(file)))
            .ToList();

        var undisposed = new List<string>();
        var checkedCount = 0;

        foreach (var file in RepoSource.FilesUnder(ViewModelFolder))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in DisposableViewModel.Matches(source))
            {
                var viewModel = match.Groups["name"].Value;
                var owner = pages.FirstOrDefault(page => page.Source.Contains($"App.GetService<{viewModel}>()"));
                if (owner.Path is null)
                    continue;

                checkedCount++;
                if (!owner.Source.Contains("ViewModel.Dispose()"))
                    undisposed.Add($"{RepoSource.Relative(owner.Path)} never disposes its {viewModel}");
            }
        }

        undisposed.Should().BeEmpty("a page that navigates away must let go of the view model it created");
        checkedCount.Should().BeGreaterThan(10, "this test is worthless if it found no disposable view models to check");
    }

    [Fact]
    public void NoViewModelClaimsToDisposeSomethingWhenItDisposesNothing()
    {
        var empty = new List<string>();

        foreach (var file in RepoSource.FilesUnder(ViewModelFolder))
        {
            var source = File.ReadAllText(file);
            foreach (Match match in DisposeBody.Matches(source))
            {
                var statements = match.Groups["body"].Value
                    .Split('\n')
                    .Select(line => line.Trim())
                    .Where(line => line.Length > 0 && !line.StartsWith("//") && line != "GC.SuppressFinalize(this);")
                    .ToList();

                if (statements.Count == 0)
                    empty.Add($"{RepoSource.Relative(file)} implements Dispose() but releases nothing");
            }
        }

        empty.Should().BeEmpty(
            "IDisposable tells every reader and every page that there is something to release; if there is not, it should not be there");
    }
}
