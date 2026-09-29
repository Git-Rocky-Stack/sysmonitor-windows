using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SysMonitor.Core.Services.Settings;

/// <summary>
/// The one place the application's settings live, in the packaged build and the unpackaged one alike.
/// <para>
/// There used to be two. The Settings page saved to <c>ApplicationData.LocalSettings</c> whenever the app ran
/// packaged - installed from its MSIX package - and to <c>%LocalAppData%\SysMonitor\settings.json</c> otherwise,
/// while the alert service, the minimize-to-tray check and Auto Game Mode only ever read settings.json. In the
/// packaged build nothing wrote that file, so turning notifications off, moving an alert threshold or switching
/// off minimize-to-tray was saved and then ignored. In the unpackaged build the Settings page rewrote the whole
/// file from a copy it took when the page opened, so a custom game Auto Game Mode had added in the meantime was
/// erased the next time Save was pressed.
/// </para>
/// <para>
/// Every reader and writer now shares this store, and it keeps one file in one format in both builds. A save
/// reads the file again and writes only what changed since the last save on top of it, so it never erases a
/// key another writer saved before it. The first time the packaged build starts with this store, the settings
/// earlier versions kept in LocalSettings are copied across, so updating loses nothing.
/// </para>
/// </summary>
public sealed class SettingsStore : ISettingsStore
{
    /// <summary>Written once LocalSettings has been copied across, so it is never consulted again.</summary>
    internal const string ImportedMarker = "PackageSettingsImported";

    /// <summary>
    /// How long a write can go on sharing its timestamp with the next one. A write time is only as fine as the
    /// clock that sets it - the system timer's tick, about 16 ms by default, or 2 s on a FAT volume - so two
    /// saves close enough together leave the same stamp.
    /// </summary>
    private static readonly TimeSpan StampResolution = TimeSpan.FromSeconds(3);

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Func<IReadOnlyDictionary<string, object>?>? _readPackageSettings;
    private readonly Action? _clearPackageSettings;
    private readonly ILogger _logger;
    private readonly object _gate = new();

    // What has changed since the last save: the keys set, and whether everything was cleared first. A save
    // lays exactly this over what is on disk.
    private readonly HashSet<string> _set = new(StringComparer.Ordinal);
    private bool _clearPending;

    private Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);
    private bool _loaded;
    private DateTime _stamp;
    private bool _stampSettled;

    /// <summary>The per-user file the application keeps its settings in.</summary>
    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SysMonitor", "settings.json");

    /// <summary>The application's store: <see cref="DefaultPath"/>, taking over LocalSettings when packaged.</summary>
    public SettingsStore(ILogger<SettingsStore>? logger = null)
        : this(DefaultPath, ReadLocalSettings, EmptyLocalSettings, logger)
    {
    }

    /// <summary>A store over a file of the caller's choosing - how a test keeps away from the real settings.</summary>
    internal SettingsStore(string path, ILogger? logger = null)
        : this(path, readPackageSettings: null, clearPackageSettings: null, logger)
    {
    }

    /// <summary>
    /// The seam the tests drive. The two delegates stand in for LocalSettings: the first returns what an earlier
    /// packaged version saved there, or null when the app is not running packaged; the second empties it.
    /// </summary>
    internal SettingsStore(string path, Func<IReadOnlyDictionary<string, object>?>? readPackageSettings,
        Action? clearPackageSettings, ILogger? logger)
    {
        _path = path;
        _readPackageSettings = readPackageSettings;
        _clearPackageSettings = clearPackageSettings;
        _logger = logger ?? NullLogger.Instance;
    }

    public T Get<T>(string key, T defaultValue)
    {
        lock (_gate)
        {
            Refresh();
            if (!_values.TryGetValue(key, out var element))
                return defaultValue;

            try
            {
                return element.Deserialize<T>() ?? defaultValue;
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
            {
                _logger.LogDebug(ex, "Setting {Key} does not read as {Type}; using its default", key, typeof(T).Name);
                return defaultValue;
            }
        }
    }

    public void Set<T>(string key, T value)
    {
        lock (_gate)
        {
            _values[key] = JsonSerializer.SerializeToElement(value);
            _set.Add(key);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            // The marker stays: without it the next start would copy the old LocalSettings back in.
            var cleared = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            if (_values.TryGetValue(ImportedMarker, out var marker))
                cleared[ImportedMarker] = marker;

            _values = cleared;
            _set.Clear();
            _clearPending = true;
        }
    }

    public bool Save()
    {
        lock (_gate)
        {
            // Another writer may have saved since this store last read the file - another process, or this one
            // before a restart. Reading it again first is what keeps a save from erasing keys it never touched.
            if (!Refresh(force: true))
            {
                _logger.LogWarning("Settings were not saved: {Path} could not be read, and writing over a file " +
                    "whose contents are unknown could erase settings saved there", _path);
                return false;
            }

            if (!Write(_values))
                return false;

            if (_clearPending)
                EmptyPackageSettings();

            _set.Clear();
            _clearPending = false;
            return true;
        }
    }

    /// <summary>
    /// Re-reads the file when it may have changed since this store last read it. False when it is there but
    /// could not be read; what was read before then stands.
    /// </summary>
    private bool Refresh(bool force = false)
    {
        var stamp = StampOnDisk();
        if (!force && _loaded && _stampSettled && stamp == _stamp)
            return true;

        var values = ReadFile();
        if (values is null)
            return false;

        Remember(stamp);

        // Copied across on the first read and written at once, on its own, so LocalSettings is read on one
        // start and no other - whatever happens to the changes laid over it below.
        if (!_loaded && ImportPackageSettings(values))
            Write(values);

        // Changes not yet saved stay on top of whatever is on disk.
        if (_clearPending)
        {
            values = values.Where(pair => pair.Key == ImportedMarker)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        }

        foreach (var key in _set)
            values[key] = _values[key];

        _values = values;
        _loaded = true;
        return true;
    }

    /// <summary>
    /// Notes the file's timestamp as of the last read or write. A timestamp only tells two versions of the file
    /// apart once the clock has moved past it, so until then the next read looks at the file itself.
    /// </summary>
    private void Remember(DateTime stamp)
    {
        _stamp = stamp;
        _stampSettled = DateTime.UtcNow - stamp >= StampResolution;
    }

    /// <summary>
    /// Copies what an earlier packaged version saved in LocalSettings into <paramref name="values"/>, keeping
    /// anything the file already holds. True when anything - even just the marker - was added.
    /// </summary>
    private bool ImportPackageSettings(Dictionary<string, JsonElement> values)
    {
        if (_readPackageSettings is null || values.ContainsKey(ImportedMarker))
            return false;

        var packaged = _readPackageSettings();
        if (packaged is null)
            return false;

        foreach (var (key, value) in packaged)
        {
            if (value is null || values.ContainsKey(key))
                continue;

            try
            {
                values[key] = JsonSerializer.SerializeToElement(value, value.GetType());
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
            {
                _logger.LogDebug(ex, "Setting {Key} could not be copied across from LocalSettings", key);
            }
        }

        values[ImportedMarker] = JsonSerializer.SerializeToElement(true);
        return true;
    }

    /// <summary>Empties LocalSettings after a clear, so no copy of the cleared settings is left behind there.</summary>
    private void EmptyPackageSettings()
    {
        try
        {
            _clearPackageSettings?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "LocalSettings could not be emptied; nothing reads it once it has been copied across");
        }
    }

    /// <summary>The file's contents; null when it is there but could not be read, which is not the same as empty.</summary>
    private Dictionary<string, JsonElement>? ReadFile()
    {
        try
        {
            if (!File.Exists(_path))
                return new Dictionary<string, JsonElement>(StringComparer.Ordinal);

            // Shared for writing and deleting, so a reader never stands in the way of another store's save.
            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(stream);
            return parsed is null
                ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException ex)
        {
            // Not settings at all. Every setting reads as its default, and the next save replaces the file.
            _logger.LogWarning(ex, "{Path} does not hold valid settings; every setting reads as its default", _path);
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Settings could not be read from {Path}", _path);
            return null;
        }
    }

    /// <summary>Writes the whole set through a temporary file, so a crash mid-write cannot leave half a file.</summary>
    private bool Write(Dictionary<string, JsonElement> values)
    {
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(temporary, JsonSerializer.Serialize(values, WriteOptions));
            File.Move(temporary, _path, overwrite: true);
            Remember(StampOnDisk());
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Settings could not be saved to {Path}", _path);
            TryDelete(temporary);
            return false;
        }
    }

    private void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "Could not remove {Path}", path);
        }
    }

    /// <summary>When the file was last written; a file that is not there reads as <see cref="DateTime.MinValue"/>.</summary>
    private DateTime StampOnDisk()
    {
        try
        {
            return File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Unknown, so it cannot vouch for anything: the next read looks at the file itself.
            _logger.LogDebug(ex, "Could not check {Path} for changes", _path);
            return DateTime.MaxValue;
        }
    }

    /// <summary>What an earlier packaged version saved, or null when the app is not running packaged.</summary>
    private static IReadOnlyDictionary<string, object>? ReadLocalSettings()
    {
        try
        {
            return Windows.Storage.ApplicationData.Current.LocalSettings.Values
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }
        catch (Exception)
        {
            // Unpackaged, ApplicationData.Current throws: there is no package, so there is nothing to copy across.
            return null;
        }
    }

    private static void EmptyLocalSettings() =>
        Windows.Storage.ApplicationData.Current.LocalSettings.Values.Clear();
}
