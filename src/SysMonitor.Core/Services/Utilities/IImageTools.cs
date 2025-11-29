namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Service for image operations - compression, conversion, resize
/// </summary>
public interface IImageTools
{
    Task<ImageOperationResult> CompressImageAsync(string inputPath, int quality = 80, IProgress<int>? progress = null);
    Task<ImageOperationResult> ConvertImageAsync(string inputPath, ImageFormat targetFormat, int quality = 90, IProgress<int>? progress = null);
    Task<ImageOperationResult> ResizeImageAsync(string inputPath, int maxWidth, int maxHeight, bool maintainAspectRatio = true, IProgress<int>? progress = null);
    Task<ImageOperationResult> BatchConvertAsync(IEnumerable<string> inputPaths, ImageFormat targetFormat, int quality = 90, IProgress<BatchImageProgress>? progress = null);
    Task<ImageOperationResult> BatchResizeAsync(IEnumerable<string> inputPaths, int maxWidth, int maxHeight, bool maintainAspectRatio = true, IProgress<BatchImageProgress>? progress = null);
    Task<ImageInfo?> GetImageInfoAsync(string filePath);
    bool IsValidImage(string filePath);
    string[] SupportedFormats { get; }
}

// Enums
public enum ImageFormat
{
    Jpeg,
    Png,
    Bmp,
    Gif,
    WebP,
    Ico
}

// Models
public record ImageOperationResult
{
    public bool Success { get; init; }
    public string OutputPath { get; init; } = "";
    public long OriginalSize { get; init; }
    public long NewSize { get; init; }
    public double CompressionRatio { get; init; }
    public string ErrorMessage { get; init; } = "";
    public List<string> OutputFiles { get; init; } = [];
}

public record ImageInfo
{
    public string FileName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int Width { get; init; }
    public int Height { get; init; }
    public string Dimensions => $"{Width} × {Height}";
    public long FileSizeBytes { get; init; }
    public string FormattedSize { get; init; } = "";
    public string Format { get; init; } = "";
    public string ColorDepth { get; init; } = "";
    public bool HasAlpha { get; init; }
    public DateTime? CreatedDate { get; init; }
    public DateTime? ModifiedDate { get; init; }
    public double AspectRatio => Height > 0 ? Math.Round((double)Width / Height, 2) : 0;
    public string Resolution { get; init; } = "";
}

public record BatchImageProgress
{
    public int ProcessedCount { get; init; }
    public int TotalCount { get; init; }
    public string CurrentFile { get; init; } = "";
    public string Status { get; init; } = "";
    public bool IsComplete => ProcessedCount >= TotalCount;
    public double PercentComplete => TotalCount > 0 ? ProcessedCount * 100.0 / TotalCount : 0;
}
