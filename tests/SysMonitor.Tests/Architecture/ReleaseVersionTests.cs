using System.Text.RegularExpressions;
using FluentAssertions;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Architecture;

/// <summary>
/// The version a user sees has to be the version they installed.
/// <para>
/// It was written out by hand in four places and three of them were stale. <c>Build-Release.ps1</c> named
/// the installer and the portable zip 1.0.0; <c>installer\SysMonitorSetup.iss</c> — the script all three
/// build paths actually compile — declared 1.0.0, so Add/Remove Programs and
/// <c>HKLM\SOFTWARE\…\Version</c> both reported 1.0.0 for a 2.2.2 build. A bug report against "1.0.0" names
/// a release that never existed.
/// </para>
/// <para>
/// Inno Setup's <c>GetFileVersion</c> reads the version out of the executable being packaged, so the
/// installer cannot disagree with the binary it ships. This test holds the remaining hand-written copies to
/// the csproj, which is the source they all derive from.
/// </para>
/// </summary>
public class ReleaseVersionTests
{
    private const string Csproj = "src/SysMonitor.App/SysMonitor.App.csproj";
    private const string Manifest = "src/SysMonitor.App/Package.appxmanifest";
    private const string BuildScript = "Build-Release.ps1";

    [Fact]
    public void ThePackageManifestCarriesTheVersionTheProjectDeclares()
    {
        var version = ProjectVersion();

        var manifest = Read(Manifest);
        var declared = Regex.Match(manifest, @"Version=""(?<v>\d+\.\d+\.\d+\.\d+)""");
        declared.Success.Should().BeTrue("Package.appxmanifest must declare an Identity version");

        declared.Groups["v"].Value.Should().Be($"{version}.0",
            "the Store manifest and the assembly are the same build");
    }

    [Fact]
    public void TheBuildScriptDoesNotCarryAVersionOfItsOwn()
    {
        var script = Read(BuildScript);

        var hardCoded = Regex.Match(script, @"^\s*\$AppVersion\s*=\s*""(?<v>[\d\.]+)""", RegexOptions.Multiline);

        if (hardCoded.Success)
        {
            hardCoded.Groups["v"].Value.Should().Be(ProjectVersion(),
                "a version written out by hand in the build script is one more copy to go stale");
        }
        else
        {
            script.Should().Contain("SysMonitor.App.csproj",
                "the build script has to read the version from somewhere, and the csproj is where it lives");
        }
    }

    [Fact]
    public void TheInstallerScriptTheBuildCompilesTakesItsVersionFromTheBinary()
    {
        var compiled = CompiledInstallerScripts();
        compiled.Should().NotBeEmpty("this test is worthless if it cannot find the script the build compiles");

        foreach (var relative in compiled)
        {
            var script = Read(relative);

            var hardCoded = Regex.Match(script, @"^#define\s+MyAppVersion\s+""(?<v>[\d\.]+)""", RegexOptions.Multiline);
            if (hardCoded.Success)
            {
                hardCoded.Groups["v"].Value.Should().Be(ProjectVersion(),
                    $"{relative} ships a version the app does not have");
            }
            else
            {
                script.Should().Contain("GetFileVersion",
                    $"{relative} must read the version from the executable it packages");
            }
        }
    }

    [Fact]
    public void ThereIsOneInstallerScript_AndItIsTheOneTheBuildCompiles()
    {
        var onDisk = Directory.GetFiles(Path.Combine(RepoSource.Root, "installer"), "*.iss")
            .Select(RepoSource.Relative)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var compiled = CompiledInstallerScripts();

        onDisk.Should().BeEquivalentTo(compiled,
            "a second installer script is a second AppId, and Inno Setup treats a different AppId as a " +
            "different product - installing from both leaves two copies side by side instead of upgrading");
    }

    [Fact]
    public void TheInstallerReadmeNamesTheScriptThatIsActuallyCompiled()
    {
        var readme = Read("installer/INSTALLER_README.txt");
        var compiled = CompiledInstallerScripts().Select(Path.GetFileName).ToList();

        foreach (var name in compiled)
            readme.Should().Contain(name!, "the readme has to point at the script the build uses");

        foreach (var orphan in Directory.GetFiles(Path.Combine(RepoSource.Root, "installer"), "*.iss")
                     .Select(Path.GetFileName)
                     .Where(name => !compiled.Contains(name)))
        {
            readme.Should().NotContain(orphan!, "the readme must not name a script nothing compiles");
        }
    }

    /// <summary>
    /// The download page names a build that exists.
    /// <para>
    /// This is the same defect one layer out. The installer cannot disagree with the binary any more, but
    /// the page offering it can: a version bump that does not reach <c>docs/</c> leaves the site linking to
    /// <c>.../releases/download/v3.0.0/STX1-SystemMonitor-Setup-3.0.0.exe</c> for a 3.0.1 release, publishing
    /// the checksum of a file nobody can download any more, and telling the reader to verify one filename
    /// against another. The link 404s or, worse, serves the previous build.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDownloadTheDocumentationOffersNamesTheVersionTheProjectBuilds()
    {
        var version = ProjectVersion();

        // Every way a release artifact is named: the two file names and the tag folder they live under.
        var artifact = new Regex(
            @"STX1-SystemMonitor-Setup-(?<v>\d+\.\d+\.\d+)\.exe"
            + @"|STX1-SystemMonitor-(?<v>\d+\.\d+\.\d+)-Portable-x64\.zip"
            + @"|/releases/download/v(?<v>\d+\.\d+\.\d+)/");

        var stale = new List<string>();
        var named = 0;

        foreach (var document in PublishedDocuments())
        {
            var lines = File.ReadAllLines(document);

            for (var number = 1; number <= lines.Length; number++)
            {
                foreach (Match match in artifact.Matches(lines[number - 1]))
                {
                    named++;
                    if (match.Groups["v"].Value != version)
                        stale.Add($"{RepoSource.Relative(document)}:{number} offers {match.Value}");
                }
            }
        }

        named.Should().BeGreaterThan(5,
            "this test is worthless if it cannot find the downloads it is meant to check");
        stale.Should().BeEmpty(
            $"the project builds {version}; a document offering any other build links to a file that " +
            "release does not contain");
    }

    /// <summary>
    /// The changelog's newest release is the one being built. A version bumped in the csproj with the
    /// changes still sitting under [Unreleased] ships a build whose release notes do not exist.
    /// </summary>
    [Fact]
    public void TheChangelogsNewestReleaseIsTheVersionTheProjectBuilds()
    {
        var changelog = Read("CHANGELOG.md");

        var released = Regex.Matches(changelog, @"^## \[(?<v>\d+\.\d+\.\d+)\]", RegexOptions.Multiline)
            .Select(match => match.Groups["v"].Value)
            .ToList();

        released.Should().NotBeEmpty("this test is worthless if it cannot find a release heading");
        released[0].Should().Be(ProjectVersion(),
            "the newest released section of the changelog is the release notes for this build");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Documents a reader downloads from, or is told to verify a download against.</summary>
    private static IReadOnlyList<string> PublishedDocuments()
    {
        var paths = new List<string>
        {
            Path.Combine(RepoSource.Root, "README.md"),
            Path.Combine(RepoSource.Root, "FEATURES_AND_USER_GUIDE.md"),
            Path.Combine(RepoSource.Root, "CHANGELOG.md"),
        };

        var docs = Path.Combine(RepoSource.Root, "docs");
        foreach (var pattern in new[] { "*.md", "*.html", "*.txt", "*.xml" })
            paths.AddRange(Directory.EnumerateFiles(docs, pattern, SearchOption.TopDirectoryOnly));

        return paths.Where(File.Exists).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The version in the app's csproj: the one every other copy derives from.</summary>
    private static string ProjectVersion()
    {
        var match = Regex.Match(Read(Csproj), @"<Version>(?<v>\d+\.\d+\.\d+)</Version>");
        match.Success.Should().BeTrue($"{Csproj} must declare a <Version>");
        return match.Groups["v"].Value;
    }

    /// <summary>Every .iss the repository's build scripts hand to the Inno Setup compiler.</summary>
    private static List<string> CompiledInstallerScripts()
    {
        var scripts = new[] { "Build-Release.ps1", "installer/build-installer.ps1", "installer/build-installer.bat" };
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var script in scripts)
        {
            // Strip the path prefixes the scripts build the name from, so the batch file's
            // "%~dp0SysMonitorSetup.iss" is read as the file name and not as "dp0SysMonitorSetup.iss".
            var text = Read(script)
                .Replace("%~dp0", "/", StringComparison.Ordinal)
                .Replace("$ScriptDir", "/", StringComparison.Ordinal);

            foreach (Match match in Regex.Matches(text, @"(?<![\w\-])[\w\-]+\.iss"))
                referenced.Add($"installer/{match.Value}");
        }

        return referenced.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Read(string relative) =>
        File.ReadAllText(Path.Combine(RepoSource.Root, relative.Replace('/', Path.DirectorySeparatorChar)));
}
