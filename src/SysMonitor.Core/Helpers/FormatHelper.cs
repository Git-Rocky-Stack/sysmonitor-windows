namespace SysMonitor.Core.Helpers;

/// <summary>
/// Provides common formatting utilities used across the application.
/// </summary>
public static class FormatHelper
{
    /// <summary>
    /// Formats a byte count into a human-readable string (B, KB, MB, GB, TB).
    /// </summary>
    /// <param name="bytes">The number of bytes to format.</param>
    /// <param name="decimalPlaces">Number of decimal places (default: 2).</param>
    /// <returns>A formatted string representation of the size.</returns>
    public static string FormatSize(long bytes, int decimalPlaces = 2)
    {
        if (bytes < 0)
            return "0 B";

        string[] suffixes = ["B", "KB", "MB", "GB", "TB", "PB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return suffixIndex == 0
            ? $"{bytes} B"
            : $"{size.ToString($"F{decimalPlaces}")} {suffixes[suffixIndex]}";
    }

    /// <summary>
    /// Formats a speed in bytes per second to a human-readable string.
    /// </summary>
    /// <param name="bytesPerSecond">Speed in bytes per second.</param>
    /// <returns>Formatted speed string.</returns>
    public static string FormatSpeed(double bytesPerSecond)
    {
        if (bytesPerSecond >= 1_000_000_000)
            return $"{bytesPerSecond / 1_000_000_000:F1} GB/s";
        if (bytesPerSecond >= 1_000_000)
            return $"{bytesPerSecond / 1_000_000:F1} MB/s";
        if (bytesPerSecond >= 1_000)
            return $"{bytesPerSecond / 1_000:F1} KB/s";
        return $"{bytesPerSecond:F0} B/s";
    }

    /// <summary>
    /// Formats a TimeSpan into a human-readable uptime string.
    /// </summary>
    /// <param name="uptime">The time span to format.</param>
    /// <returns>Formatted uptime string (e.g., "5d 3h 20m").</returns>
    public static string FormatUptime(TimeSpan uptime)
    {
        if (uptime.TotalDays >= 1)
            return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";
        if (uptime.TotalHours >= 1)
            return $"{uptime.Hours}h {uptime.Minutes}m";
        return $"{uptime.Minutes}m";
    }

    /// <summary>
    /// Formats a percentage value with consistent decimal places.
    /// </summary>
    /// <param name="percent">The percentage value (0-100).</param>
    /// <param name="decimalPlaces">Number of decimal places (default: 1).</param>
    /// <returns>Formatted percentage string.</returns>
    public static string FormatPercent(double percent, int decimalPlaces = 1)
    {
        return $"{percent.ToString($"F{decimalPlaces}")}%";
    }

    /// <summary>
    /// Formats a temperature in Celsius to Fahrenheit string.
    /// </summary>
    /// <param name="celsius">Temperature in Celsius.</param>
    /// <param name="showUnit">Whether to include the unit suffix.</param>
    /// <returns>Formatted temperature string in Fahrenheit.</returns>
    public static string FormatTemperatureF(double celsius, bool showUnit = true)
    {
        if (celsius <= 0)
            return showUnit ? "N/A" : "0";

        var fahrenheit = (celsius * 1.8) + 32;
        return showUnit ? $"{fahrenheit:F0}°F" : $"{fahrenheit:F0}";
    }

    /// <summary>
    /// Formats a temperature in Celsius.
    /// </summary>
    /// <param name="celsius">Temperature in Celsius.</param>
    /// <param name="showUnit">Whether to include the unit suffix.</param>
    /// <returns>Formatted temperature string in Celsius.</returns>
    public static string FormatTemperatureC(double celsius, bool showUnit = true)
    {
        if (celsius <= 0)
            return showUnit ? "N/A" : "0";

        return showUnit ? $"{celsius:F0}°C" : $"{celsius:F0}";
    }

    /// <summary>
    /// Converts Celsius to Fahrenheit.
    /// </summary>
    /// <param name="celsius">Temperature in Celsius.</param>
    /// <returns>Temperature in Fahrenheit.</returns>
    public static double CelsiusToFahrenheit(double celsius)
    {
        return (celsius * 1.8) + 32;
    }
}
