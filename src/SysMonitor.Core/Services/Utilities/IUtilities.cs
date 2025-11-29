namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Service for finding large files on the system
/// </summary>
public interface ILargeFileFinder
{
    Task<List<LargeFileInfo>> ScanAsync(string path, long minSizeBytes = 100 * 1024 * 1024, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<bool> DeleteFileAsync(string filePath);
    Task<bool> MoveToRecycleBinAsync(string filePath);
}

/// <summary>
/// Service for finding duplicate files
/// </summary>
public interface IDuplicateFinder
{
    Task<List<DuplicateGroup>> ScanAsync(string path, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<long> DeleteDuplicatesAsync(IEnumerable<string> filesToDelete);
}

/// <summary>
/// Service for converting files between formats
/// </summary>
public interface IFileConverter
{
    Task<ConversionResult> ConvertImageAsync(string sourcePath, string targetFormat, string? outputPath = null, ImageConversionOptions? options = null);
    Task<ConversionResult> CompressFileAsync(string sourcePath, CompressionFormat format, string? outputPath = null);
    Task<ConversionResult> DecompressFileAsync(string sourcePath, string? outputPath = null);
    IEnumerable<string> GetSupportedImageFormats();
    IEnumerable<CompressionFormat> GetSupportedCompressionFormats();
}

// Models
public record LargeFileInfo
{
    public string FullPath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Directory { get; init; } = "";
    public long SizeBytes { get; init; }
    public string FormattedSize { get; init; } = "";
    public DateTime LastModified { get; init; }
    public string Extension { get; init; } = "";
    public string FileType { get; init; } = "";
}

public record DuplicateGroup
{
    public string Hash { get; init; } = "";
    public long FileSize { get; init; }
    public string FormattedSize { get; init; } = "";
    public List<DuplicateFileInfo> Files { get; init; } = [];
    public int DuplicateCount => Files.Count - 1;
    public long WastedSpace => FileSize * DuplicateCount;
}

public record DuplicateFileInfo
{
    public string FullPath { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Directory { get; init; } = "";
    public DateTime LastModified { get; init; }
    public bool IsOriginal { get; init; }
}

public record ScanProgress
{
    public int FilesScanned { get; init; }
    public int TotalFiles { get; init; }
    public string CurrentFile { get; init; } = "";
    public double PercentComplete => TotalFiles > 0 ? (FilesScanned * 100.0 / TotalFiles) : 0;
    public string Status { get; init; } = "";
}

public record ConversionResult
{
    public bool Success { get; init; }
    public string OutputPath { get; init; } = "";
    public long OriginalSize { get; init; }
    public long NewSize { get; init; }
    public string ErrorMessage { get; init; } = "";
    public double CompressionRatio => OriginalSize > 0 ? (1 - (NewSize / (double)OriginalSize)) * 100 : 0;
}

public record ImageConversionOptions
{
    public int Quality { get; init; } = 85;
    public int? MaxWidth { get; init; }
    public int? MaxHeight { get; init; }
    public bool PreserveAspectRatio { get; init; } = true;
}

public enum CompressionFormat
{
    Zip,
    GZip,
    SevenZip
}
