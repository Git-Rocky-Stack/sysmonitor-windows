namespace SysMonitor.Core.Helpers;

/// <summary>
/// Path checks the app relies on to stay inside a folder the user chose.
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Whether <paramref name="path"/> sits inside <paramref name="allowedBasePath"/>. The comparison is
    /// made on full paths, so "C:\Users\Test\..\Other" is outside "C:\Users\Test" however it is spelled,
    /// and a path equal to the base is not inside it.
    /// </summary>
    public static bool IsPathWithinDirectory(string? path, string? allowedBasePath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(allowedBasePath))
            return false;

        try
        {
            // A trailing separator does not make a folder a child of itself, so the candidate loses one
            // before the comparison while the base gains one.
            var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var fullBasePath = Path.GetFullPath(allowedBasePath);

            if (!fullBasePath.EndsWith(Path.DirectorySeparatorChar))
                fullBasePath += Path.DirectorySeparatorChar;

            return fullPath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            // An unusable path is not inside anything.
            return false;
        }
    }

    /// <summary>Whether two paths name the same place, ignoring a trailing separator.</summary>
    public static bool IsSamePath(string? path, string? other)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(other))
            return false;

        try
        {
            return string.Equals(
                Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(other).TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
