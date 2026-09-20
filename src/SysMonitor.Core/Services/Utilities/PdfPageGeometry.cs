using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Maps the page as a viewer displays it onto PDFsharp's drawing space, so an annotation placed on a
/// rendered page lands on the same spot of the saved PDF.
/// </summary>
/// <remarks>
/// Behaviour this relies on, measured with PDFsharp 6.1.1 and Windows.Data.Pdf (the renderer the editor
/// previews pages with), and covered end to end by PdfAnnotationPlacementTests:
/// <list type="bullet">
/// <item><description>XGraphics.FromPdfPage draws on an imported page in the page's unrotated space: (x, y)
/// lands on the PDF user-space point (x, H - y), where H is the page height XGraphics reports (the MediaBox
/// height), whatever the page's /Rotate and MediaBox origin.</description></item>
/// <item><description>A viewer shows the CropBox, clipped to the MediaBox, turned clockwise by /Rotate.</description></item>
/// </list>
/// </remarks>
public static class PdfPageGeometry
{
    /// <summary>Points (1/72 inch) per device-independent pixel (1/96 inch).</summary>
    public const double PointsPerDip = 72.0 / 96.0;

    /// <summary>
    /// The <see cref="PdfAnnotation.CoordinateScale"/> for coordinates measured on a page image rendered by
    /// <see cref="PdfPageRasterizer"/> at <paramref name="zoom"/>, where one pixel is one DIP times the zoom.
    /// </summary>
    public static double CanvasCoordinateScale(double zoom)
    {
        if (!(zoom > 0) || double.IsInfinity(zoom))
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be a positive number.");
        return PointsPerDip / zoom;
    }

    /// <summary>
    /// How many canvas units one unit of an annotation's own coordinates is worth, on a page image rendered
    /// at <paramref name="canvasZoom"/>. An annotation drawn at one zoom is put back on the canvas at another
    /// by multiplying its numbers by this, which is what lets a redraw survive a zoom.
    /// </summary>
    /// <param name="annotationCoordinateScale">The annotation's <see cref="PdfAnnotation.CoordinateScale"/>: points per unit of its own coordinates.</param>
    public static double RedrawScale(double annotationCoordinateScale, double canvasZoom)
    {
        if (!(annotationCoordinateScale > 0) || double.IsInfinity(annotationCoordinateScale))
        {
            throw new ArgumentOutOfRangeException(
                nameof(annotationCoordinateScale), annotationCoordinateScale, "A coordinate scale must be a positive number.");
        }

        return annotationCoordinateScale / CanvasCoordinateScale(canvasZoom);
    }

    /// <summary>Reduces a /Rotate value to 0, 90, 180 or 270. Values that are not multiples of 90 are invalid and count as 0.</summary>
    public static int NormalizeRotation(int degrees)
    {
        var normalized = ((degrees % 360) + 360) % 360;
        return normalized % 90 == 0 ? normalized : 0;
    }

    /// <summary>
    /// The part of the page a viewer displays, in PDF user space: the CropBox clipped to the MediaBox, or the
    /// MediaBox when the page has no usable CropBox. Reads the page dictionary directly, because the
    /// PdfPage.CropBox getter adds an empty /CropBox to a page that has none.
    /// </summary>
    internal static PageBox VisibleBox(PdfPage page, XSize drawingPageSize)
    {
        var media = PageBox.From(page.Elements.GetRectangle("/MediaBox"));
        if (media.IsEmpty)
            media = new PageBox(0, 0, drawingPageSize.Width, drawingPageSize.Height);

        var crop = PageBox.From(page.Elements.GetRectangle("/CropBox"));
        if (crop.IsEmpty)
            return media;

        var clipped = crop.Intersect(media);
        return clipped.IsEmpty ? media : clipped;
    }

    /// <summary>
    /// Transform from displayed-page coordinates (points from the top-left corner of the visible area, y down,
    /// with <paramref name="rotation"/> applied) to the coordinates XGraphics.FromPdfPage draws with.
    /// </summary>
    internal static XMatrix DisplayToDrawing(PageBox visible, double drawingPageHeight, int rotation)
    {
        var h = drawingPageHeight;
        return NormalizeRotation(rotation) switch
        {
            90 => new XMatrix(0, -1, 1, 0, visible.Left, h - visible.Bottom),
            180 => new XMatrix(-1, 0, 0, -1, visible.Right, h - visible.Bottom),
            270 => new XMatrix(0, 1, -1, 0, visible.Right, h - visible.Top),
            _ => new XMatrix(1, 0, 0, 1, visible.Left, h - visible.Top),
        };
    }

    /// <summary>Size in points of the visible area as displayed with <paramref name="rotation"/>.</summary>
    internal static XSize DisplayedSize(PageBox visible, int rotation) =>
        NormalizeRotation(rotation) is 90 or 270
            ? new XSize(visible.Height, visible.Width)
            : new XSize(visible.Width, visible.Height);

    /// <summary>A rectangle in PDF user space (y up), with its corners in order.</summary>
    internal readonly record struct PageBox(double Left, double Bottom, double Right, double Top)
    {
        public double Width => Right - Left;
        public double Height => Top - Bottom;
        public bool IsEmpty => !(Width > 0 && Height > 0);

        public static PageBox From(PdfRectangle rect) => new(
            Math.Min(rect.X1, rect.X2), Math.Min(rect.Y1, rect.Y2),
            Math.Max(rect.X1, rect.X2), Math.Max(rect.Y1, rect.Y2));

        public PageBox Intersect(PageBox other) => new(
            Math.Max(Left, other.Left), Math.Max(Bottom, other.Bottom),
            Math.Min(Right, other.Right), Math.Min(Top, other.Top));
    }
}
