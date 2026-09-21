using Microsoft.Extensions.Logging;
using SysMonitor.Core.Models;
using SysMonitor.Core.Services.Cleaners;

namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// What a scheduled run was asked to clean. Windows Task Scheduler starts the app with these switches, so the
/// side that writes the command line and the side that reads it are the same code.
/// </summary>
public sealed record ScheduledCleaningRequest
{
    /// <summary>The switch that means "clean and exit", rather than open the app.</summary>
    public const string Switch = "--scheduled-clean";

    public bool TempFiles { get; init; }
    public bool BrowserCache { get; init; }
    public bool RecycleBin { get; init; }
    public bool WindowsUpdateCache { get; init; }
    public bool ThumbnailCache { get; init; }

    /// <summary>Whether to tell the user afterwards. Task Scheduler runs this with nobody watching.</summary>
    public bool ShowNotification { get; init; } = true;

    public bool CleansAnything => TempFiles || BrowserCache || RecycleBin || WindowsUpdateCache || ThumbnailCache;

    /// <summary>The file cleaner's categories this request covers.</summary>
    public IReadOnlyList<CleanerCategory> Categories
    {
        get
        {
            var categories = new List<CleanerCategory>();
            if (TempFiles)
            {
                categories.Add(CleanerCategory.WindowsTemp);
                categories.Add(CleanerCategory.UserTemp);
            }

            if (RecycleBin) categories.Add(CleanerCategory.RecycleBin);
            if (WindowsUpdateCache) categories.Add(CleanerCategory.WindowsUpdateCache);
            if (ThumbnailCache) categories.Add(CleanerCategory.Thumbnails);
            return categories;
        }
    }

    public static ScheduledCleaningRequest From(ScheduledCleaningConfig config) => new()
    {
        TempFiles = config.CleanTempFiles,
        BrowserCache = config.CleanBrowserCache,
        RecycleBin = config.CleanRecycleBin,
        WindowsUpdateCache = config.CleanWindowsUpdateCache,
        ThumbnailCache = config.CleanThumbnailCache,
        ShowNotification = config.ShowNotification,
    };

    /// <summary>The command line to give Task Scheduler.</summary>
    public IReadOnlyList<string> ToArguments()
    {
        var arguments = new List<string> { Switch };
        if (TempFiles) arguments.Add("--temp");
        if (BrowserCache) arguments.Add("--browser");
        if (RecycleBin) arguments.Add("--recycle");
        if (WindowsUpdateCache) arguments.Add("--update");
        if (ThumbnailCache) arguments.Add("--thumbnails");
        if (!ShowNotification) arguments.Add("--silent");
        return arguments;
    }

    /// <summary>
    /// Reads a command line, or returns null when it is not a scheduled run. Takes the arguments as
    /// Environment.GetCommandLineArgs gives them, program name first.
    /// </summary>
    public static ScheduledCleaningRequest? FromCommandLine(IReadOnlyList<string> arguments)
    {
        var switches = arguments
            .Skip(1)
            .Select(argument => argument.Trim())
            .Where(argument => argument.Length > 0)
            .ToList();

        if (!switches.Any(argument => argument.Equals(Switch, StringComparison.OrdinalIgnoreCase)))
            return null;

        bool Has(string name) => switches.Any(argument => argument.Equals(name, StringComparison.OrdinalIgnoreCase));

        return new ScheduledCleaningRequest
        {
            TempFiles = Has("--temp"),
            BrowserCache = Has("--browser"),
            RecycleBin = Has("--recycle"),
            WindowsUpdateCache = Has("--update"),
            ThumbnailCache = Has("--thumbnails"),
            ShowNotification = !Has("--silent"),
        };
    }
}

/// <summary>What a scheduled run removed.</summary>
public sealed record ScheduledCleaningOutcome
{
    public long BytesCleaned { get; init; }
    public int FilesDeleted { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];

    public bool Success => Errors.Count == 0;

    /// <summary>One line for a notification or a log.</summary>
    public string Summary => Errors.Count == 0
        ? $"Freed {FormatSize(BytesCleaned)} from {FilesDeleted:N0} files."
        : $"Freed {FormatSize(BytesCleaned)} from {FilesDeleted:N0} files; {Errors.Count} item(s) could not be removed.";

    private static string FormatSize(long bytes)
    {
        string[] units = ["bytes", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes:N0} bytes" : $"{size:N1} {units[unit]}";
    }
}

/// <summary>
/// Runs a scheduled clean with no window. Task Scheduler used to start the whole app instead, with highest
/// privileges, and nothing was cleaned because nothing read the switches it was given.
/// </summary>
public sealed class ScheduledCleaningRunner
{
    private readonly ITempFileCleaner _files;
    private readonly IBrowserCacheCleaner _browserCache;
    private readonly ILogger<ScheduledCleaningRunner> _logger;

    public ScheduledCleaningRunner(ITempFileCleaner files, IBrowserCacheCleaner browserCache, ILogger<ScheduledCleaningRunner> logger)
    {
        _files = files;
        _browserCache = browserCache;
        _logger = logger;
    }

    public async Task<ScheduledCleaningOutcome> RunAsync(ScheduledCleaningRequest request)
    {
        long bytes = 0;
        var files = 0;
        var errors = new List<string>();

        var categories = request.Categories;
        if (categories.Count > 0)
        {
            try
            {
                var found = await _files.ScanAsync();
                var wanted = found.Where(item => categories.Contains(item.Category)).ToList();
                foreach (var item in wanted)
                {
                    // The cleaner acts on what is selected, and this run has already chosen.
                    item.IsSelected = true;
                }

                if (wanted.Count > 0)
                {
                    var result = await _files.CleanAsync(wanted);
                    bytes += result.BytesCleaned;
                    files += result.FilesDeleted;
                    errors.AddRange(result.Errors);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled clean: files failed");
                errors.Add($"Files: {ex.Message}");
            }
        }

        if (request.BrowserCache)
        {
            try
            {
                var found = await _browserCache.ScanAsync();
                foreach (var item in found)
                {
                    item.IsSelected = true;
                }

                if (found.Count > 0)
                {
                    var result = await _browserCache.CleanAsync(found);
                    bytes += result.BytesCleaned;
                    files += result.FilesDeleted;
                    errors.AddRange(result.Errors);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled clean: browser caches failed");
                errors.Add($"Browser caches: {ex.Message}");
            }
        }

        var outcome = new ScheduledCleaningOutcome { BytesCleaned = bytes, FilesDeleted = files, Errors = errors };
        _logger.LogInformation("Scheduled clean finished: {Summary}", outcome.Summary);
        return outcome;
    }
}
