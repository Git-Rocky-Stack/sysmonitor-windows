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

    private static readonly Regex DisposableViewModel =
        new(@"class\s+(?<name>\w+ViewModel)\s*:[^{\r\n]*\bIDisposable\b");

    /// <summary>
    /// The subscription rule judged against code whose verdict is known. Without this the rule could be
    /// silently vacuous — it matches exactly four subscriptions in the whole app, all in one file, so a
    /// regex that stopped matching would report a clean bill of health rather than a failure.
    /// </summary>
    [Fact]
    public void TheSubscriptionRuleTellsALeakFromATidyTeardown()
    {
        ViewModelLifetimeRule.LeakedSubscriptions(ViewModel(
                subscribe: "_service.Changed += OnChanged;",
                teardown: "_service.Changed -= OnChanged;"))
            .Should().BeEmpty("it is taken back in Dispose()");

        ViewModelLifetimeRule.LeakedSubscriptions(ViewModel(
                subscribe: "_service.Changed += OnChanged;",
                teardown: ""))
            .Should().ContainSingle().Which.Reason.Should().Contain("never undone");

        // The hole the old rule left: the unsubscribe exists, in a method nothing calls.
        ViewModelLifetimeRule.LeakedSubscriptions(ViewModel(
                subscribe: "_service.Changed += OnChanged;",
                teardown: "",
                extra: "private void Forgotten()\n    {\n        _service.Changed -= OnChanged;\n    }"))
            .Should().ContainSingle().Which.Reason.Should().Contain("nothing reachable from teardown calls");

        // The other hole: a lambda has no name, so no `-=` can ever match it.
        ViewModelLifetimeRule.LeakedSubscriptions(ViewModel(
                subscribe: "_service.Changed += (s, e) => Refresh();",
                teardown: ""))
            .Should().ContainSingle().Which.Reason.Should().Contain("lambda");
    }

    [Fact]
    public void EveryViewModelUndoesTheSubscriptionsItMakes()
    {
        var leaks = new List<string>();
        var checkedSubscriptions = 0;

        foreach (var file in RepoSource.FilesUnder(ViewModelFolder))
        {
            var source = File.ReadAllText(file);
            checkedSubscriptions += ViewModelLifetimeRule.SubscriptionsJudged(source);

            foreach (var leak in ViewModelLifetimeRule.LeakedSubscriptions(source))
                leaks.Add($"{RepoSource.Relative(file)}: {leak.What} {leak.Reason}");
        }

        checkedSubscriptions.Should().BeGreaterThan(3,
            "this test is worthless if it cannot find the subscriptions it is meant to judge");
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

    /// <summary>
    /// The empty-Dispose rule judged against code whose verdict is known. The old regex required the closing
    /// brace at exactly four spaces of indentation, so a <c>Dispose()</c> written any other way was not
    /// inspected at all — and, with no count guard, that read as a pass.
    /// </summary>
    [Fact]
    public void TheEmptyDisposeRuleTellsAnEmptyBodyFromAFullOne()
    {
        ViewModelLifetimeRule.EmptyDisposeBodies(ViewModelWithDispose("_timer.Stop();"))
            .Should().BeEmpty("it releases something");

        ViewModelLifetimeRule.EmptyDisposeBodies(ViewModelWithDispose(""))
            .Should().ContainSingle().Which.Reason.Should().Contain("releases nothing");

        ViewModelLifetimeRule.EmptyDisposeBodies(ViewModelWithDispose("// nothing to release yet"))
            .Should().ContainSingle("a comment is not a release");

        ViewModelLifetimeRule.EmptyDisposeBodies(ViewModelWithDispose("GC.SuppressFinalize(this);"))
            .Should().ContainSingle("suppressing the finaliser releases nothing either");

        // The indentation the old rule depended on.
        ViewModelLifetimeRule.EmptyDisposeBodies(
                "public class OddlyIndentedViewModel : IDisposable\n{\n        public void Dispose()\n        {\n        }\n}")
            .Should().ContainSingle("an empty Dispose is empty at any indentation");
    }

    [Fact]
    public void NoViewModelClaimsToDisposeSomethingWhenItDisposesNothing()
    {
        var empty = new List<string>();
        var checkedBodies = 0;

        foreach (var file in RepoSource.FilesUnder(ViewModelFolder))
        {
            var source = File.ReadAllText(file);
            checkedBodies += ViewModelLifetimeRule.DisposeBodiesFound(source);

            foreach (var finding in ViewModelLifetimeRule.EmptyDisposeBodies(source))
                empty.Add($"{RepoSource.Relative(file)} {finding.Reason}");
        }

        checkedBodies.Should().BeGreaterThan(10,
            "this test is worthless if it cannot find the Dispose bodies it is meant to judge");
        empty.Should().BeEmpty(
            "IDisposable tells every reader and every page that there is something to release; if there is not, it should not be there");
    }

    private static string ViewModel(string subscribe, string teardown, string extra = "") => $$"""
        namespace Example;

        public class SampleViewModel : IDisposable
        {
            private readonly Service _service = new();

            public SampleViewModel()
            {
                {{subscribe}}
            }

            private void OnChanged(object? sender, EventArgs e) { }

            private void Refresh() { }

            {{extra}}

            public void Dispose()
            {
                {{teardown}}
                _service.Close();
            }
        }
        """;

    private static string ViewModelWithDispose(string body) => $$"""
        namespace Example;

        public class SampleViewModel : IDisposable
        {
            public void Dispose()
            {
                {{body}}
            }
        }
        """;
}
