using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// The rule that says every resource a piece of XAML asks for is one it can reach.
/// <para>
/// A XAML element sees the resources in its own <c>Resources</c>, in each of its ancestors' <c>Resources</c>, and in
/// the application's - nothing else, and not a sibling's. It lives here rather than inside the test so the test can
/// feed it snippets whose verdict is known before trusting it with the application.
/// </para>
/// </summary>
internal static class ResourceKeyRule
{
    public readonly record struct Reference(int Line, string Key);

    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary><c>{StaticResource X}</c> and <c>{ThemeResource X}</c>, with or without <c>ResourceKey=</c>, anywhere in a value.</summary>
    private static readonly Regex MarkupReference = new(
        @"\{(?:StaticResource|ThemeResource)\s+(?:ResourceKey\s*=\s*)?(?<key>[^\s,{}]+)\s*\}", RegexOptions.Compiled);

    /// <summary>
    /// Every reference in <paramref name="document"/> that names a key neither it nor the application defines.
    /// <paramref name="openDictionary"/> opens a merged dictionary's <c>Source</c>, relative to the file naming it.
    /// </summary>
    public static IReadOnlyList<Reference> Unresolved(XDocument document, string file, IReadOnlySet<string> applicationKeys,
        Func<string, string, (string Path, XDocument Document)?> openDictionary)
    {
        var unresolved = new List<Reference>();
        foreach (var element in document.Descendants())
        {
            HashSet<string>? visible = null;
            foreach (var key in ReferencesOn(element))
            {
                if (applicationKeys.Contains(key))
                    continue;

                visible ??= VisibleKeys(element, file, openDictionary);
                if (!visible.Contains(key))
                    unresolved.Add(new Reference(((System.Xml.IXmlLineInfo)element).LineNumber, key));
            }
        }

        return unresolved;
    }

    /// <summary>The keys an element's references name: markup extensions in its values, and a StaticResource element's key.</summary>
    public static IEnumerable<string> ReferencesOn(XElement element)
    {
        foreach (var attribute in element.Attributes())
        {
            foreach (Match match in MarkupReference.Matches(attribute.Value))
                yield return match.Groups["key"].Value;
        }

        if (element.Name.LocalName is "StaticResource" or "ThemeResource" && element.Attribute("ResourceKey") is { } key)
            yield return key.Value;
    }

    /// <summary>Every key defined in the dictionaries an element can see: its own, its ancestors', and a dictionary file's root.</summary>
    public static HashSet<string> VisibleKeys(XElement element, string file,
        Func<string, string, (string Path, XDocument Document)?> openDictionary)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var opened = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var current = element; current is not null; current = current.Parent)
        {
            foreach (var resources in current.Elements().Where(child => child.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)))
                AddKeys(resources, file, keys, opened, openDictionary);

            // In a dictionary file, everything in it can see everything else in it.
            if (current.Parent is null && current.Name.LocalName == "ResourceDictionary")
                AddKeys(current, file, keys, opened, openDictionary);
        }

        return keys;
    }

    /// <summary>Every key under a dictionary, including its theme dictionaries and the files it merges in.</summary>
    public static void AddKeys(XElement dictionary, string file, HashSet<string> keys, HashSet<string> opened,
        Func<string, string, (string Path, XDocument Document)?> openDictionary)
    {
        foreach (var entry in dictionary.DescendantsAndSelf())
        {
            if (entry.Attribute(Xaml + "Key") is { } key)
                keys.Add(key.Value);

            if (entry.Name.LocalName == "ResourceDictionary" && entry.Attribute("Source") is { } source &&
                openDictionary(file, source.Value) is { } merged && opened.Add(merged.Path) && merged.Document.Root is { } root)
            {
                AddKeys(root, merged.Path, keys, opened, openDictionary);
            }
        }
    }
}
