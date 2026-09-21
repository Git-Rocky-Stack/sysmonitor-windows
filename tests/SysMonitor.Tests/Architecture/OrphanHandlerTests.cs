using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// A private method in a page's code-behind is reached in one of two ways: the XAML names it as an event
/// handler, or something in the app calls it. A method that is neither is a feature that compiles, ships,
/// and does nothing.
/// <para>
/// The PDF editor's signature tool was exactly this. <c>FinalizeSignature</c> turned the strokes the user
/// had drawn into an annotation, and <c>grep</c> found one occurrence of the name in the whole repository —
/// its own declaration. Draw a signature, save, and nothing was written; the save reported success because
/// as far as it knew there was nothing to write.
/// </para>
/// </summary>
public class OrphanHandlerTests
{
    private const string ViewFolder = "src/SysMonitor.App/Views";
    private static readonly string[] CodeFolders = ["src/SysMonitor.App", "src/SysMonitor.Core"];

    /// <summary>A private method declaration: `private [async] [static] Return Name(` .</summary>
    private static readonly Regex PrivateMethod = new(
        @"^[ \t]*private\s+(?:async\s+|static\s+|unsafe\s+|extern\s+)*(?<return>[\w<>\[\],\.\?\s]+?)\s+(?<name>\w+)\s*\(",
        RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Names the compiler or the framework calls, which no source line has to mention.</summary>
    private static readonly HashSet<string> CalledByTheFramework = new(StringComparer.Ordinal)
    {
        "InitializeComponent", "Dispose", "Finalize", "OnNavigatedTo", "OnNavigatedFrom",
        "OnPropertyChanged", "OnPropertyChanging", "ToString", "Equals", "GetHashCode",
    };

    [Fact]
    public void NoPageDeclaresAHandlerThatNothingCanReach()
    {
        // Comments are stripped first. A `<see cref="FinalizeSignatureAsync"/>` in the documentation of the
        // method that is supposed to call it reads, to a plain text search, exactly like a call - and the
        // whole point of this rule is that the documentation said it was called and nothing did.
        var everySource = string.Join("\n",
            CodeFolders.SelectMany(RepoSource.FilesUnder).Select(file => WithoutComments(File.ReadAllText(file))));

        var orphans = new List<string>();
        var checkedMethods = 0;

        foreach (var file in RepoSource.FilesUnder(ViewFolder)
                     .Where(path => path.EndsWith(".xaml.cs", StringComparison.OrdinalIgnoreCase)))
        {
            var source = File.ReadAllText(file);
            var markup = MarkupFor(file);

            foreach (Match method in PrivateMethod.Matches(source))
            {
                var name = method.Groups["name"].Value;

                if (CalledByTheFramework.Contains(name) || name.StartsWith("get_", StringComparison.Ordinal))
                    continue;

                // A local function inside another method is declared with `private`? No - but a nested
                // type's constructor can look like one. Skip anything whose "return type" is a keyword
                // that means this is not a method at all.
                if (method.Groups["return"].Value.Trim() is "class" or "record" or "struct" or "enum" or "readonly")
                    continue;

                checkedMethods++;

                // The XAML naming it as `Click="Foo"` is a call.
                if (markup is not null && Regex.IsMatch(markup, $@"=\s*""{Regex.Escape(name)}"""))
                    continue;

                // Otherwise something in the app has to mention it other than its own declaration.
                var mentions = Regex.Matches(everySource, $@"\b{Regex.Escape(name)}\b").Count;
                var declarations = Regex.Matches(everySource, $@"private\s+(?:async\s+|static\s+|unsafe\s+|extern\s+)*[\w<>\[\],\.\?\s]+?\s+{Regex.Escape(name)}\s*\(").Count;

                if (mentions <= declarations)
                    orphans.Add($"{RepoSource.Relative(file)}:{LineOf(source, method.Index)}  {name}() is declared and never reached");
            }
        }

        checkedMethods.Should().BeGreaterThan(50,
            "this test is worthless if it cannot find the handlers it is meant to judge");
        orphans.Should().BeEmpty(
            "a handler nothing calls is a button that does nothing, and it ships looking like a feature");
    }

    /// <summary>The source with <c>//</c> and <c>/* */</c> comments removed, so a name in prose is not a call.</summary>
    private static string WithoutComments(string source)
    {
        var withoutBlocks = Regex.Replace(source, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return Regex.Replace(withoutBlocks, @"//[^\r\n]*", " ");
    }

    /// <summary>The XAML paired with a code-behind file, or null when there is none.</summary>
    private static string? MarkupFor(string codeBehind)
    {
        var markup = codeBehind[..^3]; // drop ".cs", leaving "...xaml"
        return File.Exists(markup) ? File.ReadAllText(markup) : null;
    }

    private static int LineOf(string source, int at) => source.Take(at).Count(c => c == '\n') + 1;
}
