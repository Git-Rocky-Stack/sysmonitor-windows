using System.Security.Cryptography;
using System.Text;

namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// System-X's stylesheet at the revision this console is transcribed from, system-x-app@eaba14b
/// (system-x-styles.css, beside this file).
/// <para>
/// The console's sources cite that file by line - <c>.btn, :2062</c>, <c>(:545-552, Day :726-732)</c> - and a
/// line number is the one kind of reference that rots without anyone touching it: System-X moves a rule, and
/// every anchor after it now points somewhere else while still reading as though it were checked. The stylesheet
/// is vendored rather than read from a sibling checkout so the anchors can be checked by the suite on any
/// machine, and pinned by hash so an edited copy fails instead of quietly agreeing with whatever it was edited
/// to say.
/// </para>
/// </summary>
internal static class PinnedStylesheet
{
    /// <summary>The revision the console names, and the fixture's header has to name the same one.</summary>
    public const string Revision = "eaba14b";

    /// <summary>
    /// SHA-256 of the stylesheet below the fixture's header, newlines normalised to LF. Recorded from the blob
    /// <c>git -C system-x-app show eaba14b:src/styles.css</c> produces.
    /// </summary>
    public const string BodySha256 = "48b92f22c89693c81b3a2a26466afe78a00e5b661b64191a9c428c64247264a9";

    private const string FixtureFile = "tests/SysMonitor.Tests/TestSupport/system-x-styles.css";
    private const string HeaderEnd = "*/";

    private static readonly Lazy<string[]> Body = new(Read);

    /// <summary>
    /// The stylesheet's own lines, so <c>Line(2062)</c> is what a <c>:2062</c> anchor points at. The fixture's
    /// header is not part of them.
    /// </summary>
    public static IReadOnlyList<string> Lines => Body.Value;

    /// <summary>The line a <c>:N</c> anchor points at, indexed the way the anchor is written.</summary>
    public static string Line(int number)
    {
        if (number < 1 || number > Body.Value.Length)
            throw new ArgumentOutOfRangeException(nameof(number),
                $":{number} is outside the pinned stylesheet, which has {Body.Value.Length} lines.");

        return Body.Value[number - 1];
    }

    /// <summary>The lines a <c>:A-B</c> anchor covers, joined.</summary>
    public static string Slab(int first, int last) =>
        string.Join('\n', Body.Value[(first - 1)..last]);

    /// <summary>The digest the fixture actually carries now, for the test that pins it.</summary>
    public static string ActualBodySha256()
    {
        var bytes = Encoding.UTF8.GetBytes(string.Join('\n', Body.Value) + "\n");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>The fixture's header, which names the revision it was taken from.</summary>
    public static string Header()
    {
        var text = File.ReadAllText(FixturePath());
        var end = text.IndexOf(HeaderEnd, StringComparison.Ordinal);
        return end < 0 ? string.Empty : text[..end];
    }

    private static string[] Read()
    {
        var text = File.ReadAllText(FixturePath()).Replace("\r\n", "\n");
        var end = text.IndexOf(HeaderEnd, StringComparison.Ordinal);
        if (end < 0)
            throw new InvalidDataException($"{FixtureFile} has no header saying which revision it was taken from.");

        // Everything after the header's closing */ and the newline that ends that line.
        var body = text[(end + HeaderEnd.Length)..].TrimStart('\n');
        return body.TrimEnd('\n').Split('\n');
    }

    private static string FixturePath() =>
        Path.Combine(RepoSource.Root, FixtureFile.Replace('/', Path.DirectorySeparatorChar));
}
