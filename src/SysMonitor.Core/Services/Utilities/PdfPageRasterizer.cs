using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Renders PDF pages with Windows.Data.Pdf. The PDF editor previews pages with it, and annotations are
/// measured on its images (see <see cref="PdfPageGeometry.CanvasCoordinateScale"/>).
/// </summary>
public static class PdfPageRasterizer
{
    /// <summary>
    /// Pixels for a length of <paramref name="dips"/> DIPs rendered at <paramref name="zoom"/>. Windows.Data.Pdf
    /// sizes a page in DIPs as displayed (CropBox, /Rotate applied), so one pixel is one DIP times the zoom.
    /// </summary>
    public static uint ToPixels(double dips, double zoom) => (uint)Math.Max(1, Math.Round(dips * zoom));

    /// <summary>
    /// Renders page <paramref name="pageNumber"/> (1-based) of <paramref name="filePath"/> as a PNG,
    /// <paramref name="zoom"/> times its displayed size in DIPs.
    /// </summary>
    public static async Task<byte[]> RenderPngAsync(string filePath, int pageNumber, double zoom)
    {
        if (!(zoom > 0) || double.IsInfinity(zoom))
            throw new ArgumentOutOfRangeException(nameof(zoom), zoom, "Zoom must be a positive number.");

        var file = await StorageFile.GetFileFromPathAsync(filePath);
        using var stream = await file.OpenReadAsync();
        var pdf = await PdfDocument.LoadFromStreamAsync(stream);
        if (pageNumber < 1 || pageNumber > pdf.PageCount)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), pageNumber, $"The document has {pdf.PageCount} pages.");

        using var page = pdf.GetPage((uint)(pageNumber - 1));
        var options = new PdfPageRenderOptions
        {
            DestinationWidth = ToPixels(page.Size.Width, zoom),
            DestinationHeight = ToPixels(page.Size.Height, zoom),
            BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
        };

        using var output = new InMemoryRandomAccessStream();
        await page.RenderToStreamAsync(output, options);

        var bytes = new byte[output.Size];
        output.Seek(0);
        using var reader = new DataReader(output);
        await reader.LoadAsync((uint)output.Size);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
