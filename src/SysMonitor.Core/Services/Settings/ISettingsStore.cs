namespace SysMonitor.Core.Services.Settings;

/// <summary>
/// The application's saved settings: what the Settings page writes and every other part of the app reads.
/// One instance is shared by all of them, so a value written by one is the value the others read.
/// </summary>
public interface ISettingsStore
{
    /// <summary>
    /// The saved value for <paramref name="key"/>, or <paramref name="defaultValue"/> when nothing is saved
    /// under it or what is saved will not read as a <typeparamref name="T"/>.
    /// </summary>
    T Get<T>(string key, T defaultValue);

    /// <summary>Records a value. Every reader sees it at once; <see cref="Save"/> writes it to disk.</summary>
    void Set<T>(string key, T value);

    /// <summary>
    /// Forgets every saved value, so each reader falls back to its default. <see cref="Save"/> makes it last.
    /// </summary>
    void Clear();

    /// <summary>
    /// Writes what has changed since the last save on top of what is on disk, so a save never erases a key
    /// another writer saved before it. False when the settings did not reach the disk; the changes are kept,
    /// and the next save tries again.
    /// </summary>
    bool Save();
}
