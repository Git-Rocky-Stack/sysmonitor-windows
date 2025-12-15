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
    // Document Operations
    Task<PdfEditorDocument?> OpenPdfAsync(string filePath);
    Task<PdfOperationResult> SavePdfAsync(PdfEditorDocument document, string outputPath);
    Task<byte[]?> RenderPageToImageAsync(string filePath, int pageNumber, double scale = 1.0);
    Task<List<PdfPageInfo>> GetPagesInfoAsync(string filePath);
    Task<PdfOperationResult> ExportToWordAsync(PdfEditorDocument document, string outputPath);

    // Page Operations
    Task<PdfOperationResult> RotatePageAsync(PdfEditorDocument document, int pageNumber, int degrees);
    Task<PdfOperationResult> DeletePageAsync(PdfEditorDocument document, int pageNumber);
    Task<PdfOperationResult> ReorderPagesAsync(PdfEditorDocument document, int[] newOrder);
    Task<PdfOperationResult> InsertBlankPageAsync(PdfEditorDocument document, int afterPageNumber, double width = 612, double height = 792);
    Task<PdfOperationResult> DuplicatePageAsync(PdfEditorDocument document, int pageNumber);

    // Annotations
    Task<PdfOperationResult> AddTextAnnotationAsync(PdfEditorDocument document, int pageNumber, TextAnnotation annotation);
    Task<PdfOperationResult> AddHighlightAsync(PdfEditorDocument document, int pageNumber, HighlightAnnotation highlight);
    Task<PdfOperationResult> AddShapeAsync(PdfEditorDocument document, int pageNumber, ShapeAnnotation shape);
    Task<PdfOperationResult> AddFreehandAsync(PdfEditorDocument document, int pageNumber, FreehandAnnotation freehand);
    Task<PdfOperationResult> AddImageAsync(PdfEditorDocument document, int pageNumber, ImageAnnotation image);
    Task<PdfOperationResult> AddStickyNoteAsync(PdfEditorDocument document, int pageNumber, StickyNoteAnnotation note);
    Task<PdfOperationResult> AddRedactionAsync(PdfEditorDocument document, int pageNumber, RedactionAnnotation redaction);
    Task<PdfOperationResult> AddSignatureAsync(PdfEditorDocument document, int pageNumber, SignatureAnnotation signature);
    Task<PdfOperationResult> AddStampAsync(PdfEditorDocument document, int pageNumber, StampAnnotation stamp);
    Task<PdfOperationResult> AddWatermarkAsync(PdfEditorDocument document, WatermarkAnnotation watermark);
    Task<PdfOperationResult> AddLinkAsync(PdfEditorDocument document, int pageNumber, LinkAnnotation link);

    // Search
    Task<List<PdfSearchResult>> SearchTextAsync(PdfEditorDocument document, string searchText, bool caseSensitive = false);
    Task<string> ExtractTextAsync(PdfEditorDocument document, int? pageNumber = null);

    // Compression & Optimization
    Task<PdfOperationResult> CompressPdfAsync(string inputPath, string outputPath, PdfCompressionOptions? options = null);
}

// PDF Editor Models
public class PdfEditorDocument
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

/// <summary>
/// Freehand drawing annotation (pen/pencil tool)
/// </summary>
public class FreehandAnnotation : PdfAnnotation
{
    public List<PointData> Points { get; set; } = [];
    public double StrokeWidth { get; set; } = 2;
    public bool IsSmoothed { get; set; } = true;
}

/// <summary>
/// Point data for freehand drawing
/// </summary>
public class PointData
{
    public double X { get; set; }
    public double Y { get; set; }

    public PointData() { }
    public PointData(double x, double y) { X = x; Y = y; }
}

/// <summary>
/// Image annotation for inserting images/photos
/// </summary>
public class ImageAnnotation : PdfAnnotation
{
    public byte[] ImageData { get; set; } = [];
    public string ImageFormat { get; set; } = "png";
    public double Opacity { get; set; } = 1.0;
    public int Rotation { get; set; } = 0;
}

/// <summary>
/// Sticky note / comment annotation
/// </summary>
public class StickyNoteAnnotation : PdfAnnotation
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public string Author { get; set; } = "";
    public DateTime CreatedDate { get; set; } = DateTime.Now;
    public bool IsExpanded { get; set; } = false;
    public string NoteColor { get; set; } = "#FFFF88"; // Yellow sticky note
}

/// <summary>
/// Redaction annotation (black out sensitive content)
/// </summary>
public class RedactionAnnotation : PdfAnnotation
{
    public bool IsApplied { get; set; } = false;
    public string FillColor { get; set; } = "#000000";
    public string OverlayText { get; set; } = ""; // Optional text like "REDACTED"
}

/// <summary>
/// Signature annotation (handwritten signature)
/// </summary>
public class SignatureAnnotation : PdfAnnotation
{
    public List<List<PointData>> Strokes { get; set; } = []; // Multiple strokes for signature
    public double StrokeWidth { get; set; } = 2;
    public byte[]? SignatureImageData { get; set; } // Can also be an image
    public string SignerName { get; set; } = "";
    public DateTime SignedDate { get; set; } = DateTime.Now;
}

/// <summary>
/// Stamp annotation (APPROVED, CONFIDENTIAL, DRAFT, etc.)
/// </summary>
public class StampAnnotation : PdfAnnotation
{
    public StampType StampType { get; set; } = StampType.Approved;
    public string CustomText { get; set; } = "";
    public double Rotation { get; set; } = -15; // Slight angle for authentic look
    public double Opacity { get; set; } = 0.85;
    public bool ShowDate { get; set; } = true;
    public bool ShowBorder { get; set; } = true;
}

/// <summary>
/// Pre-defined stamp types
/// </summary>
public enum StampType
{
    Approved,
    Rejected,
    Confidential,
    Draft,
    Final,
    Void,
    Copy,
    Original,
    NotApproved,
    ForReview,
    Urgent,
    Completed,
    Pending,
    Custom
}

/// <summary>
/// Watermark annotation (text or image watermark)
/// </summary>
public class WatermarkAnnotation : PdfAnnotation
{
    public WatermarkType Type { get; set; } = WatermarkType.Text;
    public string Text { get; set; } = "CONFIDENTIAL";
    public byte[]? ImageData { get; set; }
    public double Opacity { get; set; } = 0.15;
    public double Rotation { get; set; } = -45;
    public double FontSize { get; set; } = 72;
    public bool ApplyToAllPages { get; set; } = true;
    public WatermarkPosition Position { get; set; } = WatermarkPosition.Center;
}

/// <summary>
/// Watermark types
/// </summary>
public enum WatermarkType
{
    Text,
    Image
}

/// <summary>
/// Watermark position options
/// </summary>
public enum WatermarkPosition
{
    Center,
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight,
    Diagonal
}

/// <summary>
/// Link annotation (hyperlink)
/// </summary>
public class LinkAnnotation : PdfAnnotation
{
    public string Url { get; set; } = "";
    public string DisplayText { get; set; } = "";
    public bool IsInternal { get; set; } = false; // Link to another page in same PDF
    public int TargetPage { get; set; } = 1;
}

/// <summary>
/// Search result in PDF
/// </summary>
public record PdfSearchResult
{
    public int PageNumber { get; init; }
    public string MatchedText { get; init; } = "";
    public string ContextBefore { get; init; } = "";
    public string ContextAfter { get; init; } = "";
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
}

/// <summary>
/// PDF compression options
/// </summary>
public class PdfCompressionOptions
{
    public bool CompressImages { get; set; } = true;
    public int ImageQuality { get; set; } = 75; // 1-100
    public bool RemoveMetadata { get; set; } = false;
    public bool RemoveAnnotations { get; set; } = false;
    public bool OptimizeFonts { get; set; } = true;
}
