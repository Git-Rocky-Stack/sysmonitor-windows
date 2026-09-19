using Microsoft.Win32;

namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// A uniquely named key under HKCU\Software\SysMonitor.Tests, deleted (with its subtree) on dispose.
/// Tests never touch any other part of the registry.
/// </summary>
internal sealed class TestRegistryKey : IDisposable
{
    private const string ParentPath = @"Software\SysMonitor.Tests";

    /// <summary>Path below HKEY_CURRENT_USER.</summary>
    public string SubPath { get; }

    /// <summary>Full name, e.g. HKEY_CURRENT_USER\Software\SysMonitor.Tests\{guid}.</summary>
    public string FullName => @"HKEY_CURRENT_USER\" + SubPath;

    public RegistryKey Key { get; }

    public TestRegistryKey()
    {
        SubPath = $@"{ParentPath}\{Guid.NewGuid():N}";
        Key = Registry.CurrentUser.CreateSubKey(SubPath, writable: true);
    }

    public void Dispose()
    {
        Key.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(SubPath, throwOnMissingSubKey: false);

        using var parent = Registry.CurrentUser.OpenSubKey(ParentPath, writable: true);
        if (parent is { SubKeyCount: 0, ValueCount: 0 })
            Registry.CurrentUser.DeleteSubKey(ParentPath, throwOnMissingSubKey: false);
    }
}
