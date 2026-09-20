namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// Reads the Recycle Bin through the shell, to check that a file a test deleted can still be got back.
/// Nothing here empties it or touches anything the test did not put there.
/// </summary>
internal static class RecycleBin
{
    private const int RecycleBinFolder = 10;
    private const int OriginalLocationColumn = 1;

    /// <summary>Whether the Recycle Bin holds an item that came from this path.</summary>
    public static bool HoldsItemFrom(string originalPath)
    {
        var expectedFolder = Path.GetDirectoryName(originalPath) ?? string.Empty;
        var expectedName = Path.GetFileNameWithoutExtension(originalPath);

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType == null)
        {
            throw new InvalidOperationException("The shell is not available to read the Recycle Bin.");
        }

        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic bin = shell.NameSpace(RecycleBinFolder);

        foreach (dynamic item in bin.Items())
        {
            string folder = bin.GetDetailsOf(item, OriginalLocationColumn);
            string name = item.Name;

            // The displayed name may have its extension hidden, so the base name is what is compared.
            if (string.Equals(folder, expectedFolder, StringComparison.OrdinalIgnoreCase) &&
                Path.GetFileNameWithoutExtension(name).Equals(expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
