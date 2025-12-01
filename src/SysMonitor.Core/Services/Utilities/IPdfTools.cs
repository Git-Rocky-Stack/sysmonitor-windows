namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Service for PDF operations - merge, split, extract, sign, convert
/// </summary>
public interface IPdfTools
{
    Task<PdfOperationResult> MergePdfsAsync(IEnumerable<string> inputPaths, string outputPath);
    Task<PdfOperationResult> SplitPdfAsync(string inputPath, string outputDirectory, SplitOptions options);
    Task<PdfOperationResult> ExtractPagesAsync(string inputPath, string outputPath, int startPage, int endPage);
    Task<PdfOperationResult> AddSignatureAsync(string inputPath, string outputPath, SignatureOptions options);
    Task<PdfOperationResult> ConvertToPdfAsync(string inputPath, string outputPath);
    Task<PdfInfo?> GetPdfInfoAsync(string filePath);
    bool IsValidPdf(string filePath);
    bool IsSupportedForConversion(string filePath);
}

/// <summary>
/// Options for adding a signature to a PDF
/// </summary>
public class SignatureOptions
{
    public byte[]? SignatureImageBytes { get; set; }
    public int PageNumber { get; set; } = 1;
    public double X { get; set; } = 10; // Percentage from left
    public double Y { get; set; } = 80; // Percentage from top
    public double Width { get; set; } = 150; // Points
    public string? SignerName { get; set; }
    public bool IncludeDate { get; set; } = true;
}

/// <summary>
/// Service for network device discovery and mapping
/// </summary>
public interface INetworkMapper
{
    Task<List<NetworkDeviceInfo>> ScanNetworkAsync(string subnet, IProgress<NetworkScanProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<NetworkDeviceInfo?> GetDeviceInfoAsync(string ipAddress);
    Task<LocalNetworkInfo> GetLocalNetworkInfoAsync();
    Task<List<PortInfo>> ScanPortsAsync(string ipAddress, int[] ports, CancellationToken cancellationToken = default);
}

// PDF Models
public record PdfOperationResult
{
    public bool Success { get; init; }
    public string OutputPath { get; init; } = "";
    public int PagesProcessed { get; init; }
    public string ErrorMessage { get; init; } = "";
    public List<string> OutputFiles { get; init; } = [];
}

public record PdfInfo
{
    public string FileName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public int PageCount { get; init; }
    public long FileSizeBytes { get; init; }
    public string FormattedSize { get; init; } = "";
    public string PdfVersion { get; init; } = "";
    public string Author { get; init; } = "";
    public string Title { get; init; } = "";
    public DateTime? CreatedDate { get; init; }
    public DateTime? ModifiedDate { get; init; }
    public bool IsEncrypted { get; init; }
}

public record SplitOptions
{
    public bool SplitAllPages { get; init; } = true;
    public int StartPage { get; init; } = 1;
    public int EndPage { get; init; } = 1;
    public int PagesPerFile { get; init; } = 1;
}

// Network Models
public record NetworkDeviceInfo
{
    public string IpAddress { get; init; } = "";
    public string MacAddress { get; init; } = "";
    public string Hostname { get; init; } = "";
    public string Manufacturer { get; init; } = "";
    public string DeviceType { get; init; } = "Unknown";
    public string DeviceIcon { get; init; } = "\uE839";
    public bool IsOnline { get; init; }
    public int ResponseTimeMs { get; init; }
    public string ResponseStatus { get; init; } = "";
    public string ResponseColor { get; init; } = "#808080";
    public DateTime LastSeen { get; init; }
    public List<PortInfo> OpenPorts { get; init; } = [];
}

public record LocalNetworkInfo
{
    public string LocalIpAddress { get; init; } = "";
    public string SubnetMask { get; init; } = "";
    public string Gateway { get; init; } = "";
    public string MacAddress { get; init; } = "";
    public string Hostname { get; init; } = "";
    public string NetworkName { get; init; } = "";
    public string DnsServer { get; init; } = "";
    public string AdapterName { get; init; } = "";
}

public record PortInfo
{
    public int Port { get; init; }
    public string ServiceName { get; init; } = "";
    public bool IsOpen { get; init; }
    public string Protocol { get; init; } = "TCP";
}

public record NetworkScanProgress
{
    public int ScannedCount { get; init; }
    public int TotalCount { get; init; }
    public string CurrentTarget { get; init; } = "";
    public string Status { get; init; } = "";
    public NetworkDeviceInfo? FoundDevice { get; init; }
    public double PercentComplete => TotalCount > 0 ? ScannedCount * 100.0 / TotalCount : 0;
}

// PDF Editor Interface
public interface IPdfEditor
{
    Task<PdfDocument?> OpenPdfAsync(string filePath);
    Task<PdfOperationResult> SavePdfAsync(PdfDocument document, string outputPath);
    Task<byte[]?> RenderPageToImageAsync(string filePath, int pageNumber, double scale = 1.0);
    Task<PdfOperationResult> RotatePageAsync(PdfDocument document, int pageNumber, int degrees);
    Task<PdfOperationResult> DeletePageAsync(PdfDocument document, int pageNumber);
    Task<PdfOperationResult> ReorderPagesAsync(PdfDocument document, int[] newOrder);
    Task<PdfOperationResult> AddTextAnnotationAsync(PdfDocument document, int pageNumber, TextAnnotation annotation);
    Task<PdfOperationResult> AddHighlightAsync(PdfDocument document, int pageNumber, HighlightAnnotation highlight);
    Task<PdfOperationResult> AddShapeAsync(PdfDocument document, int pageNumber, ShapeAnnotation shape);
    Task<List<PdfPageInfo>> GetPagesInfoAsync(string filePath);
}

// PDF Editor Models
public class PdfDocument
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public List<PdfPageInfo> Pages { get; set; } = [];
    public bool IsModified { get; set; }
    public List<PdfAnnotation> Annotations { get; set; } = [];
}

public class PdfPageInfo
{
    public int PageNumber { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public int Rotation { get; set; }
    public byte[]? ThumbnailBytes { get; set; }
}

public abstract class PdfAnnotation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int PageNumber { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Color { get; set; } = "#FF0000";
}

public class TextAnnotation : PdfAnnotation
{
    public string Text { get; set; } = "";
    public double FontSize { get; set; } = 12;
    public string FontFamily { get; set; } = "Arial";
    public bool IsBold { get; set; }
    public bool IsItalic { get; set; }
}

public class HighlightAnnotation : PdfAnnotation
{
    public double Opacity { get; set; } = 0.3;
}

public class ShapeAnnotation : PdfAnnotation
{
    public ShapeType Type { get; set; }
    public double StrokeWidth { get; set; } = 2;
    public bool IsFilled { get; set; }
    public string? FillColor { get; set; }
}

public enum ShapeType
{
    Rectangle,
    Ellipse,
    Line,
    Arrow
}
