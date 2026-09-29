using System.Buffers.Binary;
using System.Text;

namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// The Windows name records in a TrueType file - enough to know what family a XAML <c>#Family</c> reference will
/// find in it, without a font library.
/// </summary>
internal static class TrueTypeNames
{
    /// <summary>Name ID to text, from the Windows, Unicode, US English records DirectWrite reads.</summary>
    public static IReadOnlyDictionary<int, string> Read(string path)
    {
        var font = File.ReadAllBytes(path);
        var names = new Dictionary<int, string>();
        var tables = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));

        for (var table = 0; table < tables; table++)
        {
            var record = 12 + table * 16;
            if (Encoding.ASCII.GetString(font, record, 4) != "name")
                continue;

            var start = (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(record + 8));
            var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(start + 2));
            var strings = start + BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(start + 4));

            for (var entry = 0; entry < count; entry++)
            {
                var at = font.AsSpan(start + 6 + entry * 12);
                var platform = BinaryPrimitives.ReadUInt16BigEndian(at);
                var encoding = BinaryPrimitives.ReadUInt16BigEndian(at[2..]);
                var language = BinaryPrimitives.ReadUInt16BigEndian(at[4..]);
                var nameId = BinaryPrimitives.ReadUInt16BigEndian(at[6..]);
                var length = BinaryPrimitives.ReadUInt16BigEndian(at[8..]);
                var offset = BinaryPrimitives.ReadUInt16BigEndian(at[10..]);

                if (platform == 3 && encoding == 1 && language == 0x409)
                    names[nameId] = Encoding.BigEndianUnicode.GetString(font, strings + offset, length);
            }
        }

        return names;
    }

    /// <summary>
    /// The family DirectWrite files the font under: the WWS family (name 21) when there is one, else the
    /// typographic family (16), else the legacy family (1).
    /// </summary>
    public static string? Family(IReadOnlyDictionary<int, string> names) =>
        names.TryGetValue(21, out var wws) ? wws
        : names.TryGetValue(16, out var typographic) ? typographic
        : names.GetValueOrDefault(1);
}
