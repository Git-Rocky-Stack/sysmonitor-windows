using System.Collections.Concurrent;
using Microsoft.Win32;
using PdfSharp.Fonts;

namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Finds the fonts installed on this machine for PDFsharp.
/// </summary>
/// <remarks>
/// PDFsharp 6 carries no fonts of its own beyond a small built-in set: measured with 6.1.1, "Arial" and
/// "Courier New" resolve, while "Consolas", "Segoe UI" and "Helvetica" each throw
/// "No appropriate font found for family name '...'". Anything drawing with one of those - the text, CSV and
/// XML to PDF conversion, the sticky notes, the stamps - failed every time, whatever the machine had
/// installed. Windows lists its fonts in the registry, and this reads them from there.
/// </remarks>
public sealed class WindowsFontResolver : IFontResolver
{
    private const string FontRegistryKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Fonts";

    /// <summary>The family used when the one asked for is not installed. PDFsharp always resolves it.</summary>
    public const string FallbackFamily = "Arial";

    private readonly Lazy<IReadOnlyDictionary<string, string>> _installed;
    private readonly ConcurrentDictionary<string, byte[]> _loaded = new(StringComparer.OrdinalIgnoreCase);

    public WindowsFontResolver()
    {
        _installed = new Lazy<IReadOnlyDictionary<string, string>>(ReadInstalledFonts);
    }

    /// <summary>
    /// Installs this resolver once for the process. PDFsharp holds one globally, and replacing it after fonts
    /// have been used is not allowed, so a resolver already in place is left alone.
    /// </summary>
    public static void Install()
    {
        GlobalFontSettings.FontResolver ??= new WindowsFontResolver();
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        var face = FaceName(familyName, isBold, isItalic);
        if (face != null)
            return new FontResolverInfo(face);

        // A family this machine does not have is drawn in one it does, rather than throwing. The text is
        // what the user asked for; the exact typeface is not worth losing the document over.
        var fallback = FaceName(FallbackFamily, isBold, isItalic) ?? FaceName(FallbackFamily, false, false);
        return fallback == null ? null : new FontResolverInfo(fallback);
    }

    public byte[]? GetFont(string faceName)
    {
        if (_loaded.TryGetValue(faceName, out var cached))
            return cached;

        if (!_installed.Value.TryGetValue(faceName, out var path))
            return null;

        try
        {
            var bytes = File.ReadAllBytes(path);
            _loaded[faceName] = bytes;
            return bytes;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A font file that cannot be read is a font this machine does not have, as far as drawing goes.
            return null;
        }
    }

    /// <summary>Whether a family is installed under its own name, without falling back.</summary>
    public bool IsInstalled(string familyName) => FaceName(familyName, false, false) != null;

    /// <summary>
    /// The face name for a family and style, which is also the key into the installed list. Windows names
    /// faces "Consolas", "Consolas Bold", "Segoe UI Bold Italic" and so on, so the style is part of the name.
    /// </summary>
    private string? FaceName(string familyName, bool isBold, bool isItalic)
    {
        var family = familyName.Trim();

        // Most specific first: a machine with no bold cut of a family still draws it in the regular one.
        var candidates = (isBold, isItalic) switch
        {
            (true, true) => [$"{family} Bold Italic", $"{family} Bold", $"{family} Italic", family],
            (true, false) => new[] { $"{family} Bold", family },
            (false, true) => [$"{family} Italic", family],
            _ => [family],
        };

        return candidates.FirstOrDefault(_installed.Value.ContainsKey);
    }

    /// <summary>
    /// Every font Windows knows about, as face name to file. The registry lists them as
    /// "Consolas Bold (TrueType)" against a file name, which is either a full path or a name in the Windows
    /// font folder. Fonts a user installed for themselves live under HKCU and their own folder.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ReadInstalledFonts()
    {
        var fonts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        Collect(Registry.LocalMachine, Environment.GetFolderPath(Environment.SpecialFolder.Windows) + @"\Fonts");
        Collect(Registry.CurrentUser, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Fonts"));

        return fonts;

        void Collect(RegistryKey root, string fontFolder)
        {
            try
            {
                using var key = root.OpenSubKey(FontRegistryKey);
                if (key == null) return;

                foreach (var entry in key.GetValueNames())
                {
                    var file = key.GetValue(entry)?.ToString();
                    if (string.IsNullOrWhiteSpace(file)) continue;

                    var path = Path.IsPathRooted(file) ? file : Path.Combine(fontFolder, file);

                    // "Consolas Bold (TrueType)" -> "Consolas Bold". A family with several files listed under
                    // one entry - "Cambria & Cambria Math" - is registered under each of its names.
                    var names = entry.Split('(')[0].Trim();
                    foreach (var name in names.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        // OpenType collections hold several faces in one file; PDFsharp reads the first, so
                        // only the first name registered for a file is taken.
                        fonts.TryAdd(name, path);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                // Best effort: a font list that cannot be read leaves the fallback family, which PDFsharp has.
            }
        }
    }
}
