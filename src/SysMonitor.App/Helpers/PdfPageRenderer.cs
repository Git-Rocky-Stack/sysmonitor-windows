using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace SysMonitor.App.Helpers;

/// <summary>
/// Windows-specific PDF page renderer using Windows.Data.Pdf API
/// </summary>
public static class PdfPageRenderer
{
    /// <summary>
    /// Renders a PDF page to PNG image bytes
    /// </summary>
    public static async Task<byte[]?> RenderPageAsync(string filePath, int pageNumber, double scale = 1.0)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenReadAsync();
            var pdfDoc = await PdfDocument.LoadFromStreamAsync(stream);

            if (pageNumber < 1 || pageNumber > pdfDoc.PageCount)
                return null;

            using var page = pdfDoc.GetPage((uint)(pageNumber - 1));

            // Calculate render size
            var renderWidth = (uint)(page.Size.Width * scale);
            var renderHeight = (uint)(page.Size.Height * scale);

            // Ensure minimum size
            renderWidth = Math.Max(renderWidth, 100);
            renderHeight = Math.Max(renderHeight, 100);

            var options = new PdfPageRenderOptions
            {
                DestinationWidth = renderWidth,
                DestinationHeight = renderHeight,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255) // White background
            };

            using var memStream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(memStream, options);

            // Convert to byte array
            var bytes = new byte[memStream.Size];
            memStream.Seek(0);
            using var reader = new DataReader(memStream);
            await reader.LoadAsync((uint)memStream.Size);
            reader.ReadBytes(bytes);

            return bytes;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"PDF render error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Renders a PDF page thumbnail (smaller scale for sidebar)
    /// </summary>
    public static async Task<byte[]?> RenderThumbnailAsync(string filePath, int pageNumber, double maxWidth = 150)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(filePath);
            using var stream = await file.OpenReadAsync();
            var pdfDoc = await PdfDocument.LoadFromStreamAsync(stream);

            if (pageNumber < 1 || pageNumber > pdfDoc.PageCount)
                return null;

            using var page = pdfDoc.GetPage((uint)(pageNumber - 1));

            // Calculate scale to fit within maxWidth
            var scale = maxWidth / page.Size.Width;
            var renderWidth = (uint)maxWidth;
            var renderHeight = (uint)(page.Size.Height * scale);

            var options = new PdfPageRenderOptions
            {
                DestinationWidth = renderWidth,
                DestinationHeight = renderHeight,
                BackgroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255)
            };

            using var memStream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(memStream, options);

            var bytes = new byte[memStream.Size];
            memStream.Seek(0);
            using var reader = new DataReader(memStream);
            await reader.LoadAsync((uint)memStream.Size);
            reader.ReadBytes(bytes);

            return bytes;
        }
        catch
        {
            return null;
        }
    }
}
