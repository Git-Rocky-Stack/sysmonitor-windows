using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// Shutdown works by the host disposing the singletons it built. It can only dispose the ones that say they
/// are disposable, so a service that owns a window or a background loop and does not implement
/// <see cref="IDisposable"/> is simply never told the app is closing.
/// <para>
/// The FPS overlay was exactly this. Its only path to closing the window was the user toggling it off; the
/// app's own shutdown never reached it. Show the overlay, close the main window, and a WinUI app with a
/// window still open carries on running with no UI — in Task Manager and nowhere else.
/// </para>
/// </summary>
public class SingletonDisposalTests
{
    private const string Registrations = "src/SysMonitor.App/App.xaml.cs";
    private static readonly string[] TypeFolders = ["src/SysMonitor.App", "src/SysMonitor.Core"];

    /// <summary>
    /// The rule judged against code whose verdict is known, before it is trusted to say the registrations
    /// are clean.
    /// </summary>
    [Fact]
    public void TheRuleTellsAnOwnerThatCanBeShutDownFromOneThatCannot()
    {
        var sources = new Dictionary<string, string>
        {
            ["Tidy"] = """
                public class Tidy : ITidy, IDisposable
                {
                    private CancellationTokenSource? _cts;
                    public void Dispose() => _cts?.Cancel();
                }
                """,
            ["Leaky"] = """
                public class Leaky : ILeaky
                {
                    private CancellationTokenSource? _cts;
                    private OverlayWindow? _window;
                }
                """,
            ["Stateless"] = """
                public class Stateless : IStateless
                {
                    private readonly string _path = "";
                }
                """,
        };

        const string registrations = """
            services.AddSingleton<Tidy>();
            services.AddSingleton<Leaky>();
            services.AddSingleton<Stateless>();
            """;

        SingletonDisposalRule.RegisteredTypes(registrations)
            .Should().BeEquivalentTo(["Tidy", "Leaky", "Stateless"]);

        var findings = SingletonDisposalRule.UndisposableOwners(registrations, sources);

        findings.Should().ContainSingle("only Leaky owns something and cannot be shut down")
            .Which.Type.Should().Be("Leaky");
    }

    [Fact]
    public void EverySingletonThatOwnsAWindowOrALoopCanBeShutDown()
    {
        var registrations = File.ReadAllText(Path.Combine(RepoSource.Root, Registrations.Replace('/', Path.DirectorySeparatorChar)));

        var sources = new Dictionary<string, string>();
        foreach (var file in TypeFolders.SelectMany(RepoSource.FilesUnder))
            sources[Path.GetFileNameWithoutExtension(file).Replace(".xaml", "")] = File.ReadAllText(file);

        var registered = SingletonDisposalRule.RegisteredTypes(registrations);
        registered.Count.Should().BeGreaterThan(20,
            "this test is worthless if it cannot find the registrations it is meant to judge");
        registered.Count(sources.ContainsKey).Should().BeGreaterThan(20,
            "this test is worthless if it cannot find the source of the types it is meant to judge");

        SingletonDisposalRule.UndisposableOwners(registrations, sources).Select(f => $"{f.Type} {f.Reason}")
            .Should().BeEmpty("the host disposes what it built, and only what says it is disposable");
    }
}
