namespace SysMonitor.Core.Helpers;

/// <summary>
/// Provides path validation and security utilities.
/// </summary>
public static class PathHelper
{
    /// <summary>
    /// Validates that a path is safe and doesn't contain directory traversal attempts.
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <returns>True if the path is safe, false otherwise.</returns>
    public static bool IsPathSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        // Check for directory traversal patterns
        if (path.Contains(".."))
            return false;

        // Check for null bytes (used in some exploits)
        if (path.Contains('\0'))
            return false;

        try
        {
            // Normalize the path and check if it's valid
            var fullPath = Path.GetFullPath(path);
            return !string.IsNullOrEmpty(fullPath);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Validates that a path is within an allowed base directory.
    /// </summary>
    /// <param name="path">The path to validate.</param>
    /// <param name="allowedBasePath">The allowed base directory.</param>
    /// <returns>True if the path is within the allowed directory, false otherwise.</returns>
    public static bool IsPathWithinDirectory(string? path, string allowedBasePath)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(allowedBasePath))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullBasePath = Path.GetFullPath(allowedBasePath);

            // Ensure base path ends with separator for accurate comparison
            if (!fullBasePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                fullBasePath += Path.DirectorySeparatorChar;

            return fullPath.StartsWith(fullBasePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets a safe file name by removing invalid characters.
    /// </summary>
    /// <param name="fileName">The file name to sanitize.</param>
    /// <returns>A sanitized file name.</returns>
    public static string GetSafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "unnamed";

        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed" : sanitized;
    }

    /// <summary>
    /// Determines the size of a directory by summing all file sizes.
    /// </summary>
    /// <param name="path">The directory path.</param>
    /// <param name="includeSubdirectories">Whether to include subdirectories.</param>
    /// <returns>A tuple containing (total size in bytes, file count).</returns>
    public static (long size, int count) GetDirectorySize(string path, bool includeSubdirectories = true)
    {
        long size = 0;
        int count = 0;

        if (!Directory.Exists(path))
            return (0, 0);

        try
        {
            var searchOption = includeSubdirectories
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            foreach (var file in Directory.EnumerateFiles(path, "*", searchOption))
            {
                try
                {
                    var fileInfo = new FileInfo(file);
                    size += fileInfo.Length;
                    count++;
                }
                catch
                {
                    // Skip files we can't access
                }
            }
        }
        catch
        {
            // Skip directories we can't access
        }

        return (size, count);
    }
}
