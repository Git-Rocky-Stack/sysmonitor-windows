using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SysMonitor.Core.Services.Utilities;

namespace SysMonitor.Tests.TestSupport;

/// <summary>Small PDFs to place annotations on.</summary>
internal static class PdfTestFile
{
    /// <summary>US Letter, as [x1 y1 x2 y2] in PDF user space.</summary>
    public static readonly double[] Letter = [0, 0, 612, 792];

    // Where WriteNumberedPages puts each page's mark, in points of the unrotated page.
    private const double MarkFirstX = 60;
    private const double MarkStep = 80;
    private const double MarkTop = 100;
    private const double MarkWidth = 40;
    private const double MarkHeight = 30;

    /// <summary>
    /// Writes <paramref name="pageCount"/> Letter pages, each marked in its own place: page n's mark sits one
    /// step further right than page n-1's, so a rendering says which page of the file it came from.
    /// </summary>
    public static void WriteNumberedPages(string path, int pageCount)
    {
        using var document = new PdfDocument();
        for (var number = 1; number <= pageCount; number++)
        {
            var page = document.AddPage();
            page.MediaBox = Rectangle(Letter);
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(
                new XSolidBrush(XColor.FromArgb(0, 0, 255)),
                MarkFirstX + (MarkStep * (number - 1)), MarkTop, MarkWidth, MarkHeight);
        }

        document.Save(path);
    }

    /// <summary>
    /// Which page of <see cref="WriteNumberedPages"/> a rendering shows, or 0 when it carries no mark.
    /// <paramref name="rotation"/> says how the page is turned, so a page on its side can be read too.
    /// </summary>
    public static int MarkedPageNumber(RenderedPage rendered, int rotation = 0)
    {
        var mark = rendered.BoundsOf(RenderedPage.IsMark);
        if (mark is null)
            return 0;

        // Turn the mark back to where it sits on the upright page before measuring.
        var upright = mark.Value.Rotate(360 - PdfPageGeometry.NormalizeRotation(rotation), rendered.Width, rendered.Height);
        var left = upright.Left * PdfPageGeometry.PointsPerDip;
        return (int)Math.Round((left - MarkFirstX) / MarkStep) + 1;
    }

    /// <summary>
    /// Writes a one-page PDF holding a blue mark inside the visible area. The mark sits off-centre, so a box
    /// that lands on it can only be in the right place, at the right size, the right way round.
    /// </summary>
    public static void WritePageWithMark(string path, double[] mediaBox, double[]? cropBox = null, int rotate = 0) =>
        WritePage(path, mediaBox, cropBox, rotate, withMark: true);

    /// <summary>Writes a one-page PDF with nothing on it.</summary>
    public static void WriteBlankPage(string path, double[] mediaBox, double[]? cropBox = null, int rotate = 0) =>
        WritePage(path, mediaBox, cropBox, rotate, withMark: false);

    private static void WritePage(string path, double[] mediaBox, double[]? cropBox, int rotate, bool withMark)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.MediaBox = Rectangle(mediaBox);
        if (cropBox is not null)
            page.CropBox = Rectangle(cropBox);

        if (withMark)
        {
            // XGraphics draws in the page's unrotated space, y down from the top of the MediaBox and
            // ignoring its origin, so place the mark from the visible area's corners in that space.
            var visible = Visible(mediaBox, cropBox);
            var mediaHeight = mediaBox[3] - mediaBox[1];
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(
                new XSolidBrush(XColor.FromArgb(0, 0, 255)),
                visible[0] + (0.25 * Width(visible)),
                mediaHeight - visible[3] + (0.30 * Height(visible)),
                0.15 * Width(visible),
                0.12 * Height(visible));
        }

        page.Rotate = rotate;
        document.Save(path);
    }

    /// <summary>
    /// Writes a one-page PDF whose /Rotate and /CropBox sit on the page tree node instead of the page, which
    /// readers must inherit. Holds the same blue mark, in the same spot as <see cref="WritePageWithMark"/>
    /// would put it for this geometry.
    /// </summary>
    public static void WritePageWithInheritedAttributes(string path)
    {
        double[] mediaBox = [0, 0, 612, 792];
        double[] cropBox = [36, 72, 576, 756];
        var visible = Visible(mediaBox, cropBox);

        // A content stream works in PDF user space (y up), so the mark is placed from the visible area directly.
        var x = visible[0] + (0.25 * Width(visible));
        var height = 0.12 * Height(visible);
        var y = visible[3] - (0.30 * Height(visible)) - height;
        var content = $"0 0 1 rg {x:0.##} {y:0.##} {0.15 * Width(visible):0.##} {height:0.##} re f\n";

        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 /MediaBox [0 0 612 792] /CropBox [36 72 576 756] /Rotate 90 >>",
            "<< /Type /Page /Parent 2 0 R /Contents 4 0 R /Resources << >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}endstream",
        };

        // Everything here is ASCII, so a character is a byte and offsets are string lengths.
        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new int[objects.Length];
        for (var i = 0; i < objects.Length; i++)
        {
            offsets[i] = pdf.Length;
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }

        var startXref = pdf.Length;
        pdf.Append("xref\n0 ").Append(objects.Length + 1).Append('\n').Append("0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n")
           .Append(startXref).Append("\n%%EOF\n");

        File.WriteAllText(path, pdf.ToString(), Encoding.Latin1);
    }

    private static PdfRectangle Rectangle(double[] box) => new(new XPoint(box[0], box[1]), new XPoint(box[2], box[3]));

    /// <summary>The area a viewer shows: the CropBox clipped to the MediaBox, else the MediaBox.</summary>
    private static double[] Visible(double[] mediaBox, double[]? cropBox) => cropBox is null
        ? mediaBox
        :
        [
            Math.Max(mediaBox[0], cropBox[0]), Math.Max(mediaBox[1], cropBox[1]),
            Math.Min(mediaBox[2], cropBox[2]), Math.Min(mediaBox[3], cropBox[3]),
        ];

    private static double Width(double[] box) => box[2] - box[0];

    private static double Height(double[] box) => box[3] - box[1];
}
