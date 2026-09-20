using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace SysMonitor.Core.Services.Utilities;

public class PdfEditor : IPdfEditor
{
    private readonly ILogger _logger;

    public PdfEditor(ILogger<PdfEditor>? logger = null)
    {
        _logger = logger ?? NullLogger<PdfEditor>.Instance;
    }

    public async Task<PdfEditorDocument?> OpenPdfAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                using var pdfDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);

                var document = new PdfEditorDocument
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    IsModified = false
                };

                for (int i = 0; i < pdfDoc.PageCount; i++)
                {
                    var page = pdfDoc.Pages[i];
                    var rotation = PdfPageGeometry.NormalizeRotation(page.Rotate);
                    document.Pages.Add(new PdfPageInfo
                    {
                        PageNumber = i + 1,
                        Width = page.Width.Point,
                        Height = page.Height.Point,
                        Rotation = rotation,
                        OriginalRotation = rotation
                    });
                }

                return document;
            }
            catch
            {
                return null;
            }
        });
    }

    public async Task<PdfOperationResult> SavePdfAsync(PdfEditorDocument document, string outputPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (string.IsNullOrEmpty(document.FilePath) || !File.Exists(document.FilePath))
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = "Source PDF file not found"
                    };
                }

                using var inputDoc = PdfReader.Open(document.FilePath, PdfDocumentOpenMode.Import);
                using var outputDoc = new PdfDocument();

                // Pictures drawn into the output have to stay readable until it is written out.
                var pageImages = new List<MemoryStream>();

                // Write the pages in the order the editor holds them
                foreach (var pageInfo in document.Pages)
                {
                    var pageAnnotations = document.Annotations.Where(a => a.PageId == pageInfo.Id).ToList();
                    var redactions = pageAnnotations.OfType<RedactionAnnotation>().ToList();

                    PdfPage page;
                    int sourceRotation;
                    XImage? pageImage = null;
                    var imageSize = new XSize();

                    if (pageInfo.IsBlank)
                    {
                        page = outputDoc.AddPage();
                        page.Width = XUnit.FromPoint(pageInfo.Width);
                        page.Height = XUnit.FromPoint(pageInfo.Height);
                        sourceRotation = 0;
                    }
                    else if (pageInfo.PageNumber > inputDoc.PageCount)
                    {
                        return new PdfOperationResult
                        {
                            Success = false,
                            ErrorMessage = $"This document expects page {pageInfo.PageNumber} of \"{document.FileName}\", which has only {inputDoc.PageCount} pages. Nothing was saved."
                        };
                    }
                    else if (redactions.Count > 0)
                    {
                        // A redacted page is replaced by a picture of itself with the boxes painted onto the
                        // pixels. Drawing a box over the page would leave the text under it in the file,
                        // where anyone can select, copy or extract it.
                        var sourcePage = inputDoc.Pages[pageInfo.PageNumber - 1];
                        sourceRotation = PdfPageGeometry.NormalizeRotation(sourcePage.Rotate);
                        var sourceVisible = PdfPageGeometry.VisibleBox(sourcePage, new XSize(sourcePage.Width.Point, sourcePage.Height.Point));
                        imageSize = PdfPageGeometry.DisplayedSize(sourceVisible, sourceRotation);

                        // A stream of its own, not one wrapped around a byte array: PDFsharp reads the buffer.
                        var stream = new MemoryStream();
                        stream.Write(RedactedPagePng(document.FilePath, pageInfo.PageNumber, redactions));
                        stream.Position = 0;
                        pageImages.Add(stream);
                        pageImage = XImage.FromStream(stream);

                        page = outputDoc.AddPage();
                        page.Width = XUnit.FromPoint(imageSize.Width);
                        page.Height = XUnit.FromPoint(imageSize.Height);
                    }
                    else
                    {
                        page = outputDoc.AddPage(inputDoc.Pages[pageInfo.PageNumber - 1]);

                        // Annotations are measured on the page as displayed with the rotation it has in the
                        // source, so take that before applying the new one.
                        sourceRotation = PdfPageGeometry.NormalizeRotation(page.Rotate);
                    }

                    // Always apply the rotation: a page turned back upright must not keep the source's /Rotate.
                    page.Rotate = pageImage != null
                        // The picture already shows the page turned the way the source file turns it, so only
                        // what the user changed since is left to apply.
                        ? PdfPageGeometry.NormalizeRotation(pageInfo.Rotation - sourceRotation)
                        : PdfPageGeometry.NormalizeRotation(pageInfo.Rotation);

                    if (pageAnnotations.Count > 0 || pageImage != null)
                    {
                        using var gfx = XGraphics.FromPdfPage(page);

                        XMatrix displayToDrawing;
                        XSize displayedSize;
                        if (pageImage != null)
                        {
                            gfx.DrawImage(pageImage, 0, 0, imageSize.Width, imageSize.Height);

                            // The new page is exactly the page as it was displayed, so annotation coordinates
                            // need no turning or shifting, only their own scale.
                            displayToDrawing = XMatrix.Identity;
                            displayedSize = imageSize;
                        }
                        else
                        {
                            var visible = PdfPageGeometry.VisibleBox(page, gfx.PageSize);
                            displayToDrawing = PdfPageGeometry.DisplayToDrawing(visible, gfx.PageSize.Height, sourceRotation);
                            displayedSize = PdfPageGeometry.DisplayedSize(visible, sourceRotation);
                        }

                        foreach (var annotation in pageAnnotations)
                        {
                            var scale = annotation.CoordinateScale;
                            if (!(scale > 0) || double.IsInfinity(scale))
                            {
                                return new PdfOperationResult
                                {
                                    Success = false,
                                    ErrorMessage = $"An annotation on page {pageInfo.PageNumber} has an invalid coordinate scale ({scale}). Nothing was saved."
                                };
                            }

                            var state = gfx.Save();
                            gfx.MultiplyTransform(displayToDrawing);
                            gfx.ScaleTransform(scale);
                            DrawAnnotation(gfx, annotation, new XSize(displayedSize.Width / scale, displayedSize.Height / scale), pageImage != null);
                            gfx.Restore(state);
                        }
                    }
                }

                // Count the pages first: a saved PdfDocument refuses every further access.
                var pagesProcessed = outputDoc.PageCount;
                if (pagesProcessed == 0)
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = "A PDF must have at least one page. Nothing was saved."
                    };
                }

                outputDoc.Save(outputPath);

                foreach (var image in pageImages)
                    image.Dispose();

                return new PdfOperationResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    PagesProcessed = pagesProcessed,
                    OutputFiles = [outputPath]
                };
            }
            catch (Exception ex)
            {
                return new PdfOperationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        });
    }

    /// <summary>
    /// How finely a redacted page is drawn before its boxes are painted on, in dots per inch. That picture
    /// replaces the page, so this trades sharpness against file size.
    /// </summary>
    private const double RedactedPageDpi = 200;

    /// <summary>
    /// A picture of a page with its redactions painted onto the pixels. What was under them is not in the
    /// result at all - which is the point, since a box drawn over text leaves the text in the file.
    /// </summary>
    private byte[] RedactedPagePng(string filePath, int sourcePageNumber, IEnumerable<RedactionAnnotation> redactions)
    {
        var zoom = RedactedPageDpi / 96.0;
        var png = PdfPageRasterizer.RenderPngAsync(filePath, sourcePageNumber, zoom).GetAwaiter().GetResult();
        var pixelsPerPoint = zoom / PdfPageGeometry.PointsPerDip;

        using var source = new MemoryStream(png);
        using var bitmap = new System.Drawing.Bitmap(source);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
        {
            foreach (var redaction in redactions)
            {
                var color = ParseColor(redaction.FillColor);

                // A redaction is never see-through, whatever colour it was given.
                using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(255, color.R, color.G, color.B));
                var scale = redaction.CoordinateScale * pixelsPerPoint;
                graphics.FillRectangle(
                    brush,
                    (float)(redaction.X * scale), (float)(redaction.Y * scale),
                    (float)(redaction.Width * scale), (float)(redaction.Height * scale));
            }
        }

        using var output = new MemoryStream();
        bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return output.ToArray();
    }

    public async Task<byte[]?> RenderPageToImageAsync(string filePath, int pageNumber, double scale = 1.0)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                using var pdfDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);

                if (pageNumber < 1 || pageNumber > pdfDoc.PageCount)
                    return null;

                var page = pdfDoc.Pages[pageNumber - 1];
                var width = (int)(page.Width.Point * scale);
                var height = (int)(page.Height.Point * scale);

                // Create a preview representation
                // Note: Full PDF rendering requires Windows-specific libraries like Windows.Data.Pdf
                // This creates a simple placeholder preview with page info
                var previewWidth = Math.Max(width, 200);
                var previewHeight = Math.Max(height, 280);

                // Create a simple PNG image manually (minimal PNG with text info)
                // Using a PDF page as temporary canvas to generate preview
                using var tempDoc = new PdfDocument();
                var tempPage = tempDoc.AddPage();
                tempPage.Width = XUnit.FromPoint(previewWidth);
                tempPage.Height = XUnit.FromPoint(previewHeight);

                using var gfx = XGraphics.FromPdfPage(tempPage);

                // White background
                gfx.DrawRectangle(XBrushes.White, 0, 0, previewWidth, previewHeight);

                // Draw border
                gfx.DrawRectangle(XPens.LightGray, 0, 0, previewWidth - 1, previewHeight - 1);

                // Draw PDF icon
                var iconFont = new XFont("Segoe UI", 40, XFontStyleEx.Regular);
                gfx.DrawString("\u25A1", iconFont, XBrushes.LightGray,
                    previewWidth / 2 - 20,
                    previewHeight / 2 - 40);

                // Draw page number indicator
                var font = new XFont("Segoe UI", 14, XFontStyleEx.Bold);
                var text = $"Page {pageNumber}";
                var textSize = gfx.MeasureString(text, font);
                gfx.DrawString(text, font, XBrushes.Gray,
                    (previewWidth - textSize.Width) / 2,
                    previewHeight / 2 + 10);

                // Draw page dimensions
                var smallFont = new XFont("Segoe UI", 10, XFontStyleEx.Regular);
                var dimText = $"{(int)page.Width.Point} x {(int)page.Height.Point} pt";
                var dimSize = gfx.MeasureString(dimText, smallFont);
                gfx.DrawString(dimText, smallFont, XBrushes.Gray,
                    (previewWidth - dimSize.Width) / 2,
                    previewHeight / 2 + 30);

                // Save PDF to memory and return as placeholder
                // Since we can't directly render to image, return the PDF bytes
                // The UI will need to handle this or show a placeholder
                using var ms = new MemoryStream();
                tempDoc.Save(ms, false);

                // Return a simple placeholder PNG instead
                // Create minimal valid PNG bytes for a gray placeholder
                return CreatePlaceholderPng(previewWidth, previewHeight, pageNumber, (int)page.Width.Point, (int)page.Height.Point);
            }
            catch
            {
                return null;
            }
        });
    }

    private static byte[] CreatePlaceholderPng(int width, int height, int pageNum, int pdfWidth, int pdfHeight)
    {
        // Create a simple gray PNG placeholder
        // This is a minimal PNG file with solid color
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // PNG Signature
        bw.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // Limit dimensions for placeholder
        width = Math.Min(width, 300);
        height = Math.Min(height, 400);

        // IHDR chunk
        var ihdr = new byte[13];
        WriteInt32BE(ihdr, 0, width);
        WriteInt32BE(ihdr, 4, height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // color type (RGB)
        ihdr[10] = 0; // compression
        ihdr[11] = 0; // filter
        ihdr[12] = 0; // interlace
        WriteChunk(bw, "IHDR", ihdr);

        // IDAT chunk (image data) - simple gray fill
        using var dataMs = new MemoryStream();
        using var compressor = new System.IO.Compression.DeflateStream(dataMs, System.IO.Compression.CompressionLevel.Fastest, true);

        for (int y = 0; y < height; y++)
        {
            compressor.WriteByte(0); // filter byte
            for (int x = 0; x < width; x++)
            {
                // Light gray background with darker border
                byte gray = (x < 2 || x >= width - 2 || y < 2 || y >= height - 2) ? (byte)180 : (byte)240;
                compressor.WriteByte(gray); // R
                compressor.WriteByte(gray); // G
                compressor.WriteByte(gray); // B
            }
        }
        compressor.Flush();
        compressor.Close();

        var zlibData = new byte[dataMs.Length + 6];
        zlibData[0] = 0x78; // zlib header
        zlibData[1] = 0x9C;
        dataMs.Position = 0;
        dataMs.Read(zlibData, 2, (int)dataMs.Length);

        // Adler32 checksum (simplified)
        uint adler = 1;
        zlibData[zlibData.Length - 4] = (byte)(adler >> 24);
        zlibData[zlibData.Length - 3] = (byte)(adler >> 16);
        zlibData[zlibData.Length - 2] = (byte)(adler >> 8);
        zlibData[zlibData.Length - 1] = (byte)adler;

        WriteChunk(bw, "IDAT", zlibData);

        // IEND chunk
        WriteChunk(bw, "IEND", Array.Empty<byte>());

        return ms.ToArray();
    }

    private static void WriteInt32BE(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void WriteChunk(BinaryWriter bw, string type, byte[] data)
    {
        // Length
        bw.Write(BitConverter.IsLittleEndian
            ? BitConverter.GetBytes(data.Length).Reverse().ToArray()
            : BitConverter.GetBytes(data.Length));

        // Type
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        bw.Write(typeBytes);

        // Data
        bw.Write(data);

        // CRC32
        var crcData = new byte[typeBytes.Length + data.Length];
        Array.Copy(typeBytes, 0, crcData, 0, typeBytes.Length);
        Array.Copy(data, 0, crcData, typeBytes.Length, data.Length);
        var crc = CalculateCrc32(crcData);
        bw.Write(BitConverter.IsLittleEndian
            ? BitConverter.GetBytes(crc).Reverse().ToArray()
            : BitConverter.GetBytes(crc));
    }

    private static uint CalculateCrc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }
        return ~crc;
    }

    public async Task<PdfOperationResult> RotatePageAsync(PdfEditorDocument document, int pagePosition, int degrees)
    {
        return await Task.Run(() =>
        {
            try
            {
                var page = PageAt(document, pagePosition);
                if (page == null)
                {
                    return NoSuchPage(pagePosition);
                }

                page.Rotation = PdfPageGeometry.NormalizeRotation(page.Rotation + degrees);

                document.IsModified = true;

                return new PdfOperationResult
                {
                    Success = true,
                    PagesProcessed = 1
                };
            }
            catch (Exception ex)
            {
                return new PdfOperationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        });
    }

    public async Task<PdfOperationResult> DeletePageAsync(PdfEditorDocument document, int pagePosition)
    {
        return await Task.Run(() =>
        {
            try
            {
                var page = PageAt(document, pagePosition);
                if (page == null)
                {
                    return NoSuchPage(pagePosition);
                }

                if (document.Pages.Count <= 1)
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = "Cannot delete the only page in the document"
                    };
                }

                document.Pages.Remove(page);

                // Remove annotations for this page
                document.Annotations.RemoveAll(a => a.PageId == page.Id);

                document.IsModified = true;

                return new PdfOperationResult
                {
                    Success = true,
                    PagesProcessed = document.Pages.Count
                };
            }
            catch (Exception ex)
            {
                return new PdfOperationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        });
    }

    public async Task<PdfOperationResult> ReorderPagesAsync(PdfEditorDocument document, int[] newOrder)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (newOrder.Length != document.Pages.Count)
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = "New order must contain all pages"
                    };
                }

                var pagesCopy = document.Pages.ToList();
                document.Pages.Clear();

                foreach (var originalIndex in newOrder)
                {
                    if (originalIndex >= 0 && originalIndex < pagesCopy.Count)
                    {
                        document.Pages.Add(pagesCopy[originalIndex]);
                    }
                }

                document.IsModified = true;

                return new PdfOperationResult
                {
                    Success = true,
                    PagesProcessed = document.Pages.Count
                };
            }
            catch (Exception ex)
            {
                return new PdfOperationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        });
    }

    /// <summary>The 1-based position of the page an annotation sits on, or 0 when that page is gone.</summary>
    private static int PositionOf(PdfEditorDocument document, PdfAnnotation annotation) =>
        document.Pages.FindIndex(p => p.Id == annotation.PageId) + 1;

    /// <summary>The page at a 1-based position in the document, or null when there is none.</summary>
    private static PdfPageInfo? PageAt(PdfEditorDocument document, int pagePosition) =>
        pagePosition >= 1 && pagePosition <= document.Pages.Count ? document.Pages[pagePosition - 1] : null;

    private static PdfOperationResult NoSuchPage(int pagePosition) =>
        new() { Success = false, ErrorMessage = $"This document has no page {pagePosition}." };

    /// <summary>Binds an annotation to the page at a position, so it stays with that page when pages move.</summary>
    private static PdfOperationResult AddAnnotation(PdfEditorDocument document, int pagePosition, PdfAnnotation annotation)
    {
        var page = PageAt(document, pagePosition);
        if (page == null)
            return NoSuchPage(pagePosition);

        annotation.PageId = page.Id;
        document.Annotations.Add(annotation);
        document.IsModified = true;

        return new PdfOperationResult { Success = true, PagesProcessed = 1 };
    }

    public async Task<PdfOperationResult> AddTextAnnotationAsync(PdfEditorDocument document, int pagePosition, TextAnnotation annotation)
    {
        return await Task.Run(() => AddAnnotation(document, pagePosition, annotation));
    }

    public async Task<PdfOperationResult> AddHighlightAsync(PdfEditorDocument document, int pagePosition, HighlightAnnotation highlight)
    {
        return await Task.Run(() => AddAnnotation(document, pagePosition, highlight));
    }

    public async Task<PdfOperationResult> AddShapeAsync(PdfEditorDocument document, int pagePosition, ShapeAnnotation shape)
    {
        return await Task.Run(() => AddAnnotation(document, pagePosition, shape));
    }

    public async Task<PdfOperationResult> AddFreehandAsync(PdfEditorDocument document, int pagePosition, FreehandAnnotation freehand)
    {
        return await Task.Run(() => AddAnnotation(document, pagePosition, freehand));
    }

    public async Task<PdfOperationResult> AddImageAsync(PdfEditorDocument document, int pagePosition, ImageAnnotation image)
    {
        return await Task.Run(() => AddAnnotation(document, pagePosition, image));
    }

    public async Task<PdfOperationResult> AddStickyNoteAsync(PdfEditorDocument document, int pagePosition, StickyNoteAnnotation note)
    {
        return await Task.Run(() => AddAnnotation(document, pagePosition, note));
    }

    public async Task<PdfOperationResult> AddRedactionAsync(PdfEditorDocument document, int pagePosition, RedactionAnnotation redaction)
    {
        return await Task.Run(() => AddAnnotation(document, pagePosition, redaction));
    }

    public async Task<PdfOperationResult> AddSignatureAsync(PdfEditorDocument document, int pagePosition, SignatureAnnotation signature)
    {
        return await Task.Run(() => AddAnnotation(document, pagePosition, signature));
    }

    public async Task<PdfOperationResult> ExportAnnotationReportAsync(PdfEditorDocument document, string outputPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                using var wordDocument = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
                var mainPart = wordDocument.AddMainDocumentPart();
                mainPart.Document = new Document();
                var body = mainPart.Document.AppendChild(new Body());

                // Add document title
                var titlePara = body.AppendChild(new Paragraph());
                var titleRun = titlePara.AppendChild(new Run());
                titleRun.AppendChild(new RunProperties(new Bold(), new FontSize { Val = "36" }));
                titleRun.AppendChild(new Text(Path.GetFileNameWithoutExtension(document.FileName)));

                // Add page count info
                var infoPara = body.AppendChild(new Paragraph());
                var infoRun = infoPara.AppendChild(new Run());
                infoRun.AppendChild(new Text($"Exported from PDF: {document.FileName}"));
                body.AppendChild(new Paragraph()); // Empty line

                infoRun = body.AppendChild(new Paragraph()).AppendChild(new Run());
                infoRun.AppendChild(new Text($"Total Pages: {document.Pages.Count}"));

                var scopeRun = body.AppendChild(new Paragraph()).AppendChild(new Run());
                scopeRun.AppendChild(new RunProperties(new Italic()));
                scopeRun.AppendChild(new Text(
                    "This report lists the pages of the PDF and the annotations added to them. " +
                    "The text of the pages themselves is not included."));
                body.AppendChild(new Paragraph()); // Empty line

                // Add page content markers
                for (var position = 1; position <= document.Pages.Count; position++)
                {
                    var page = document.Pages[position - 1];

                    // Page separator
                    var pagePara = body.AppendChild(new Paragraph());
                    var pageRun = pagePara.AppendChild(new Run());
                    pageRun.AppendChild(new RunProperties(new Bold()));
                    pageRun.AppendChild(new Text($"--- Page {position} ({page.Width:F0} x {page.Height:F0} pt) ---"));

                    // Add annotations for this page
                    var pageAnnotations = document.Annotations.Where(a => a.PageId == page.Id).ToList();
                    if (pageAnnotations.Any())
                    {
                        var annotPara = body.AppendChild(new Paragraph());
                        var annotRun = annotPara.AppendChild(new Run());
                        annotRun.AppendChild(new RunProperties(new Italic()));
                        annotRun.AppendChild(new Text($"Annotations on this page: {pageAnnotations.Count}"));

                        foreach (var annotation in pageAnnotations)
                        {
                            var annContent = body.AppendChild(new Paragraph());
                            var annRun = annContent.AppendChild(new Run());

                            var annotationText = annotation switch
                            {
                                TextAnnotation ta => $"  • Text: \"{ta.Text}\"",
                                StickyNoteAnnotation sn => $"  • Note: {sn.Title} - {sn.Content}",
                                RedactionAnnotation ra => $"  • [REDACTED CONTENT]",
                                FreehandAnnotation => $"  • Freehand drawing",
                                HighlightAnnotation => $"  • Highlighted area",
                                ShapeAnnotation sa => $"  • Shape: {sa.Type}",
                                ImageAnnotation => $"  • Inserted image",
                                SignatureAnnotation sg => $"  • Signature by: {sg.SignerName}",
                                _ => $"  • Annotation at ({annotation.X:F0}, {annotation.Y:F0})"
                            };

                            annRun.AppendChild(new Text(annotationText));
                        }
                    }

                    body.AppendChild(new Paragraph()); // Empty line between pages

                    // Add page break after each page (except last)
                    if (position < document.Pages.Count)
                    {
                        var breakPara = body.AppendChild(new Paragraph());
                        breakPara.AppendChild(new Run(new Break { Type = BreakValues.Page }));
                    }
                }

                // Add footer note
                body.AppendChild(new Paragraph());
                var footerPara = body.AppendChild(new Paragraph());
                var footerRun = footerPara.AppendChild(new Run());
                footerRun.AppendChild(new RunProperties(new Italic(), new FontSize { Val = "20" }));
                footerRun.AppendChild(new Text($"Exported from SysMonitor PDF Editor on {DateTime.Now:yyyy-MM-dd HH:mm:ss}"));

                mainPart.Document.Save();

                return new PdfOperationResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    PagesProcessed = document.Pages.Count,
                    OutputFiles = [outputPath]
                };
            }
            catch (Exception ex)
            {
                return new PdfOperationResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        });
    }

    public async Task<List<PdfPageInfo>> GetPagesInfoAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            var pages = new List<PdfPageInfo>();

            try
            {
                if (!File.Exists(filePath))
                    return pages;

                using var pdfDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);

                for (int i = 0; i < pdfDoc.PageCount; i++)
                {
                    var page = pdfDoc.Pages[i];
                    var rotation = PdfPageGeometry.NormalizeRotation(page.Rotate);
                    pages.Add(new PdfPageInfo
                    {
                        PageNumber = i + 1,
                        Width = page.Width.Point,
                        Height = page.Height.Point,
                        Rotation = rotation,
                        OriginalRotation = rotation
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "GetPagesInfoAsync failed");
            }

            return pages;
        });
    }

    /// <summary>
    /// Draws an annotation in its own coordinates. The caller has set up the transform from those coordinates
    /// to the page; <paramref name="pageSize"/> is the displayed page in the same units (for page-relative placement).
    /// </summary>
    /// <param name="boxesAlreadyPainted">
    /// True when the page has been redrawn as a picture with the redaction boxes painted onto its pixels, so
    /// only their labels are left to draw.
    /// </param>
    private void DrawAnnotation(XGraphics gfx, PdfAnnotation annotation, XSize pageSize, bool boxesAlreadyPainted = false)
    {
        var color = ParseColor(annotation.Color);

        switch (annotation)
        {
            case TextAnnotation textAnn:
                DrawTextAnnotation(gfx, textAnn, color);
                break;
            case HighlightAnnotation highlightAnn:
                DrawHighlight(gfx, highlightAnn, color);
                break;
            case ShapeAnnotation shapeAnn:
                DrawShape(gfx, shapeAnn, color);
                break;
            case FreehandAnnotation freehand:
                DrawFreehand(gfx, freehand, color);
                break;
            case ImageAnnotation imageAnn:
                DrawImage(gfx, imageAnn);
                break;
            case StickyNoteAnnotation noteAnn:
                DrawStickyNote(gfx, noteAnn);
                break;
            case RedactionAnnotation redactAnn:
                DrawRedaction(gfx, redactAnn, boxesAlreadyPainted);
                break;
            case SignatureAnnotation sigAnn:
                DrawSignature(gfx, sigAnn, color);
                break;
            case StampAnnotation stampAnn:
                DrawStamp(gfx, stampAnn);
                break;
            case WatermarkAnnotation watermarkAnn:
                DrawWatermark(gfx, watermarkAnn, pageSize);
                break;
            case LinkAnnotation linkAnn:
                DrawLink(gfx, linkAnn);
                break;
        }
    }

    private void DrawFreehand(XGraphics gfx, FreehandAnnotation annotation, XColor color)
    {
        if (annotation.Points.Count < 2) return;

        var pen = new XPen(color, annotation.StrokeWidth)
        {
            LineCap = XLineCap.Round,
            LineJoin = XLineJoin.Round
        };

        // Draw connected line segments
        for (int i = 1; i < annotation.Points.Count; i++)
        {
            var p1 = annotation.Points[i - 1];
            var p2 = annotation.Points[i];
            gfx.DrawLine(pen, p1.X, p1.Y, p2.X, p2.Y);
        }
    }

    private void DrawImage(XGraphics gfx, ImageAnnotation annotation)
    {
        if (annotation.ImageData == null || annotation.ImageData.Length == 0) return;

        try
        {
            using var ms = new MemoryStream(annotation.ImageData);
            var image = XImage.FromStream(ms);

            // Apply opacity if needed
            if (annotation.Opacity < 1.0)
            {
                // PdfSharp doesn't directly support opacity for images, draw as-is
            }

            gfx.DrawImage(image, annotation.X, annotation.Y, annotation.Width, annotation.Height);
        }
        catch
        {
            // If image fails to load, draw placeholder
            gfx.DrawRectangle(XPens.Gray, XBrushes.LightGray, annotation.X, annotation.Y, annotation.Width, annotation.Height);
            var font = new XFont("Arial", 8, XFontStyleEx.Regular);
            gfx.DrawString("[Image]", font, XBrushes.Gray, annotation.X + 5, annotation.Y + annotation.Height / 2);
        }
    }

    private void DrawStickyNote(XGraphics gfx, StickyNoteAnnotation annotation)
    {
        var noteColor = ParseColor(annotation.NoteColor);
        var brush = new XSolidBrush(noteColor);

        // Draw note background with folded corner effect
        var path = new XGraphicsPath();
        var foldSize = 15;
        path.AddLine(annotation.X, annotation.Y, annotation.X + annotation.Width - foldSize, annotation.Y);
        path.AddLine(annotation.X + annotation.Width - foldSize, annotation.Y, annotation.X + annotation.Width, annotation.Y + foldSize);
        path.AddLine(annotation.X + annotation.Width, annotation.Y + foldSize, annotation.X + annotation.Width, annotation.Y + annotation.Height);
        path.AddLine(annotation.X + annotation.Width, annotation.Y + annotation.Height, annotation.X, annotation.Y + annotation.Height);
        path.CloseFigure();
        gfx.DrawPath(brush, path);

        // Draw fold triangle
        var foldBrush = new XSolidBrush(XColor.FromArgb(50, 0, 0, 0));
        var foldPath = new XGraphicsPath();
        foldPath.AddLine(annotation.X + annotation.Width - foldSize, annotation.Y, annotation.X + annotation.Width, annotation.Y + foldSize);
        foldPath.AddLine(annotation.X + annotation.Width, annotation.Y + foldSize, annotation.X + annotation.Width - foldSize, annotation.Y + foldSize);
        foldPath.CloseFigure();
        gfx.DrawPath(foldBrush, foldPath);

        // Draw border
        gfx.DrawRectangle(XPens.DarkGoldenrod, annotation.X, annotation.Y, annotation.Width, annotation.Height);

        // Draw title
        var titleFont = new XFont("Arial", 10, XFontStyleEx.Bold);
        var contentFont = new XFont("Arial", 9, XFontStyleEx.Regular);
        var textBrush = XBrushes.Black;

        if (!string.IsNullOrEmpty(annotation.Title))
        {
            gfx.DrawString(annotation.Title, titleFont, textBrush, annotation.X + 5, annotation.Y + 14);
        }

        // Draw content (truncated if needed)
        if (!string.IsNullOrEmpty(annotation.Content))
        {
            var contentY = annotation.Y + 28;
            var lines = annotation.Content.Split('\n').Take(5); // Limit to 5 lines
            foreach (var line in lines)
            {
                var truncated = line.Length > 30 ? line.Substring(0, 27) + "..." : line;
                gfx.DrawString(truncated, contentFont, textBrush, annotation.X + 5, contentY);
                contentY += 12;
            }
        }
    }

    private void DrawRedaction(XGraphics gfx, RedactionAnnotation annotation, bool boxAlreadyPainted)
    {
        // On a page saved as a picture the box is already in the pixels, where it covers content that is no
        // longer in the file. Drawing it again would only hide the difference between the two.
        if (!boxAlreadyPainted)
        {
            var fillColor = ParseColor(annotation.FillColor);
            var brush = new XSolidBrush(fillColor);
            gfx.DrawRectangle(brush, annotation.X, annotation.Y, annotation.Width, annotation.Height);
        }

        // Draw overlay text if specified (e.g., "REDACTED")
        if (!string.IsNullOrEmpty(annotation.OverlayText))
        {
            var font = new XFont("Arial", 10, XFontStyleEx.Bold);
            var textBrush = XBrushes.White;
            var textSize = gfx.MeasureString(annotation.OverlayText, font);

            var textX = annotation.X + (annotation.Width - textSize.Width) / 2;
            var textY = annotation.Y + (annotation.Height + textSize.Height) / 2;

            gfx.DrawString(annotation.OverlayText, font, textBrush, textX, textY);
        }
    }

    private void DrawSignature(XGraphics gfx, SignatureAnnotation annotation, XColor color)
    {
        // If signature has image data, draw it
        if (annotation.SignatureImageData != null && annotation.SignatureImageData.Length > 0)
        {
            try
            {
                using var ms = new MemoryStream(annotation.SignatureImageData);
                var image = XImage.FromStream(ms);
                gfx.DrawImage(image, annotation.X, annotation.Y, annotation.Width, annotation.Height);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DrawSignature failed");
            }
        }

        // Draw handwritten signature from strokes
        if (annotation.Strokes.Count > 0)
        {
            var pen = new XPen(color, annotation.StrokeWidth)
            {
                LineCap = XLineCap.Round,
                LineJoin = XLineJoin.Round
            };

            foreach (var stroke in annotation.Strokes)
            {
                if (stroke.Count < 2) continue;

                for (int i = 1; i < stroke.Count; i++)
                {
                    var p1 = stroke[i - 1];
                    var p2 = stroke[i];
                    // Translate points relative to annotation position
                    gfx.DrawLine(pen, annotation.X + p1.X, annotation.Y + p1.Y, annotation.X + p2.X, annotation.Y + p2.Y);
                }
            }
        }

        // Draw signer name and date if provided
        if (!string.IsNullOrEmpty(annotation.SignerName))
        {
            var font = new XFont("Arial", 8, XFontStyleEx.Italic);
            var text = $"{annotation.SignerName} - {annotation.SignedDate:MM/dd/yyyy}";
            gfx.DrawString(text, font, new XSolidBrush(color), annotation.X, annotation.Y + annotation.Height + 12);
        }
    }

    private void DrawTextAnnotation(XGraphics gfx, TextAnnotation annotation, XColor color)
    {
        var style = XFontStyleEx.Regular;
        if (annotation.IsBold && annotation.IsItalic)
            style = XFontStyleEx.BoldItalic;
        else if (annotation.IsBold)
            style = XFontStyleEx.Bold;
        else if (annotation.IsItalic)
            style = XFontStyleEx.Italic;

        var font = new XFont(annotation.FontFamily, annotation.FontSize, style);
        var brush = new XSolidBrush(color);

        // (X, Y) is the top-left corner of the text, as on the editor canvas; a plain point would be the baseline.
        // The rectangle only anchors the text (TopLeft needs a non-empty layout box); it does not clip.
        var anchor = new XRect(annotation.X, annotation.Y, Math.Max(annotation.Width, 1), Math.Max(annotation.Height, 1));
        gfx.DrawString(annotation.Text, font, brush, anchor, XStringFormats.TopLeft);
    }

    private void DrawHighlight(XGraphics gfx, HighlightAnnotation annotation, XColor color)
    {
        var highlightColor = XColor.FromArgb((int)(annotation.Opacity * 255), color.R, color.G, color.B);
        var brush = new XSolidBrush(highlightColor);

        gfx.DrawRectangle(brush, annotation.X, annotation.Y, annotation.Width, annotation.Height);
    }

    private void DrawShape(XGraphics gfx, ShapeAnnotation annotation, XColor color)
    {
        var pen = new XPen(color, annotation.StrokeWidth);
        XBrush? brush = null;

        if (annotation.IsFilled && !string.IsNullOrEmpty(annotation.FillColor))
        {
            brush = new XSolidBrush(ParseColor(annotation.FillColor));
        }

        switch (annotation.Type)
        {
            case ShapeType.Rectangle:
                if (brush != null)
                    gfx.DrawRectangle(pen, brush, annotation.X, annotation.Y, annotation.Width, annotation.Height);
                else
                    gfx.DrawRectangle(pen, annotation.X, annotation.Y, annotation.Width, annotation.Height);
                break;

            case ShapeType.Ellipse:
                if (brush != null)
                    gfx.DrawEllipse(pen, brush, annotation.X, annotation.Y, annotation.Width, annotation.Height);
                else
                    gfx.DrawEllipse(pen, annotation.X, annotation.Y, annotation.Width, annotation.Height);
                break;

            case ShapeType.Line:
                gfx.DrawLine(pen, annotation.X, annotation.Y,
                    annotation.X + annotation.Width, annotation.Y + annotation.Height);
                break;

            case ShapeType.Arrow:
                DrawArrow(gfx, pen, annotation);
                break;
        }
    }

    private void DrawArrow(XGraphics gfx, XPen pen, ShapeAnnotation annotation)
    {
        var startX = annotation.X;
        var startY = annotation.Y;
        var endX = annotation.X + annotation.Width;
        var endY = annotation.Y + annotation.Height;

        // Draw the line
        gfx.DrawLine(pen, startX, startY, endX, endY);

        // Draw the arrowhead
        var angle = Math.Atan2(endY - startY, endX - startX);
        var arrowLength = 10;
        var arrowAngle = Math.PI / 6; // 30 degrees

        var x1 = endX - arrowLength * Math.Cos(angle - arrowAngle);
        var y1 = endY - arrowLength * Math.Sin(angle - arrowAngle);
        var x2 = endX - arrowLength * Math.Cos(angle + arrowAngle);
        var y2 = endY - arrowLength * Math.Sin(angle + arrowAngle);

        gfx.DrawLine(pen, endX, endY, x1, y1);
        gfx.DrawLine(pen, endX, endY, x2, y2);
    }

    private XColor ParseColor(string colorString)
    {
        try
        {
            if (colorString.StartsWith("#"))
            {
                colorString = colorString.Substring(1);
                if (colorString.Length == 6)
                {
                    var r = Convert.ToByte(colorString.Substring(0, 2), 16);
                    var g = Convert.ToByte(colorString.Substring(2, 2), 16);
                    var b = Convert.ToByte(colorString.Substring(4, 2), 16);
                    return XColor.FromArgb(r, g, b);
                }
                else if (colorString.Length == 8)
                {
                    var a = Convert.ToByte(colorString.Substring(0, 2), 16);
                    var r = Convert.ToByte(colorString.Substring(2, 2), 16);
                    var g = Convert.ToByte(colorString.Substring(4, 2), 16);
                    var b = Convert.ToByte(colorString.Substring(6, 2), 16);
                    return XColor.FromArgb(a, r, g, b);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "ParseColor failed");
        }

        return XColors.Red;
    }

    // ==================== NEW FEATURES ====================

    public async Task<PdfOperationResult> InsertBlankPageAsync(PdfEditorDocument document, int afterPagePosition, double width = 612, double height = 792)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!(width > 0) || !(height > 0))
                {
                    return new PdfOperationResult { Success = false, ErrorMessage = "A page must have a positive width and height." };
                }

                var newPage = new PdfPageInfo
                {
                    PageNumber = 0, // Comes from no page of the source file
                    Width = width,
                    Height = height,
                    Rotation = 0
                };

                var insertIndex = Math.Max(0, Math.Min(afterPagePosition, document.Pages.Count));
                document.Pages.Insert(insertIndex, newPage);
                document.IsModified = true;

                return new PdfOperationResult
                {
                    Success = true,
                    PagesProcessed = document.Pages.Count
                };
            }
            catch (Exception ex)
            {
                return new PdfOperationResult { Success = false, ErrorMessage = ex.Message };
            }
        });
    }

    public async Task<PdfOperationResult> DuplicatePageAsync(PdfEditorDocument document, int pagePosition)
    {
        return await Task.Run(() =>
        {
            try
            {
                var sourcePage = PageAt(document, pagePosition);
                if (sourcePage == null)
                {
                    return NoSuchPage(pagePosition);
                }

                var duplicatePage = new PdfPageInfo
                {
                    PageNumber = sourcePage.PageNumber, // Will copy from original
                    Width = sourcePage.Width,
                    Height = sourcePage.Height,
                    Rotation = sourcePage.Rotation,
                    OriginalRotation = sourcePage.OriginalRotation
                };

                var insertIndex = document.Pages.IndexOf(sourcePage) + 1;
                document.Pages.Insert(insertIndex, duplicatePage);

                // The copy carries the page's annotations, so a duplicate of a redacted page stays redacted.
                foreach (var annotation in document.Annotations.Where(a => a.PageId == sourcePage.Id).ToList())
                {
                    document.Annotations.Add(annotation.CopyTo(duplicatePage.Id));
                }

                document.IsModified = true;

                return new PdfOperationResult
                {
                    Success = true,
                    PagesProcessed = document.Pages.Count
                };
            }
            catch (Exception ex)
            {
                return new PdfOperationResult { Success = false, ErrorMessage = ex.Message };
            }
        });
    }

    public async Task<PdfOperationResult> AddStampAsync(PdfEditorDocument document, int pagePosition, StampAnnotation stamp)
    {
        return await Task.Run(() => AddAnnotation(document, pagePosition, stamp));
    }

    public async Task<PdfOperationResult> AddWatermarkAsync(PdfEditorDocument document, WatermarkAnnotation watermark, int pagePosition = 1)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (watermark.ApplyToAllPages)
                {
                    // Add watermark to all pages
                    foreach (var page in document.Pages)
                    {
                        var pageWatermark = new WatermarkAnnotation
                        {
                            PageId = page.Id,
                            Type = watermark.Type,
                            Text = watermark.Text,
                            ImageData = watermark.ImageData,
                            Opacity = watermark.Opacity,
                            Rotation = watermark.Rotation,
                            FontSize = watermark.FontSize,
                            ApplyToAllPages = false,
                            Position = watermark.Position,
                            Color = watermark.Color,
                            X = watermark.X,
                            Y = watermark.Y,
                            Width = watermark.Width,
                            Height = watermark.Height,
                            CoordinateScale = watermark.CoordinateScale
                        };
                        document.Annotations.Add(pageWatermark);
                    }
                }
                else
                {
                    var page = PageAt(document, pagePosition);
                    if (page == null)
                    {
                        return NoSuchPage(pagePosition);
                    }

                    watermark.PageId = page.Id;
                    document.Annotations.Add(watermark);
                }

                document.IsModified = true;
                return new PdfOperationResult { Success = true, PagesProcessed = watermark.ApplyToAllPages ? document.Pages.Count : 1 };
            }
            catch (Exception ex)
            {
                return new PdfOperationResult { Success = false, ErrorMessage = ex.Message };
            }
        });
    }

    public async Task<PdfOperationResult> AddLinkAsync(PdfEditorDocument document, int pagePosition, LinkAnnotation link)
    {
        return await Task.Run(() => AddAnnotation(document, pagePosition, link));
    }

    /// <summary>
    /// Searches the annotations on this document. PDFsharp does not extract page text and nothing here adds
    /// that, so the pages themselves are not searched - which is why this is not called SearchText.
    /// </summary>
    public async Task<List<PdfSearchResult>> SearchAnnotationsAsync(PdfEditorDocument document, string searchText, bool caseSensitive = false)
    {
        return await Task.Run(() =>
        {
            var results = new List<PdfSearchResult>();

            if (string.IsNullOrEmpty(searchText))
                return results;

            // The annotations are held in memory, so a search does not need the file - and still works when
            // it has been moved or is open elsewhere.
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            foreach (var annotation in document.Annotations)
            {
                var textContent = SearchableText(annotation);

                if (string.IsNullOrEmpty(textContent))
                    continue;

                var index = textContent.IndexOf(searchText, comparison);
                if (index < 0)
                    continue;

                results.Add(new PdfSearchResult
                {
                    PageNumber = PositionOf(document, annotation),
                    MatchedText = textContent.Substring(index, searchText.Length),
                    ContextBefore = Ellipsize(textContent[..index], keepEnd: true),
                    ContextAfter = Ellipsize(textContent[(index + searchText.Length)..], keepEnd: false),
                    X = annotation.X,
                    Y = annotation.Y,
                    Width = annotation.Width,
                    Height = annotation.Height
                });
            }

            return results;
        });
    }

    /// <summary>Carries a document's own description - what a reader sees in File > Properties - to a copy of it.</summary>
    private static void CopyDescription(PdfDocumentInformation from, PdfDocumentInformation to)
    {
        to.Title = from.Title;
        to.Author = from.Author;
        to.Subject = from.Subject;
        to.Keywords = from.Keywords;
        to.Creator = from.Creator;

        // Only a date the file actually carries; an absent one reads as DateTime.MinValue.
        if (from.Elements.ContainsKey("/CreationDate"))
            to.CreationDate = from.CreationDate;
    }

    /// <summary>
    /// The text an annotation carries, as a reader of the page would see it. Annotations that show no text -
    /// highlights, shapes, drawings, images, redactions - have none.
    /// </summary>
    private static string? SearchableText(PdfAnnotation annotation) => annotation switch
    {
        TextAnnotation text => text.Text,
        StickyNoteAnnotation note => $"{note.Title} {note.Content}".Trim(),
        StampAnnotation stamp => string.IsNullOrWhiteSpace(stamp.CustomText)
            ? stamp.StampType.ToString()
            : stamp.CustomText,
        WatermarkAnnotation watermark => watermark.Text,
        SignatureAnnotation signature => signature.SignerName,
        LinkAnnotation link => $"{link.DisplayText} {link.Url}".Trim(),
        _ => null
    };

    /// <summary>Trims a side of a match down to a readable amount of context, marking what was cut.</summary>
    private static string Ellipsize(string context, bool keepEnd)
    {
        const int maxLength = 40;
        if (context.Length <= maxLength)
            return context;

        return keepEnd
            ? "\u2026" + context[^maxLength..]
            : context[..maxLength] + "\u2026";
    }

    public async Task<PdfOperationResult> CompressPdfAsync(string inputPath, string outputPath, PdfCompressionOptions? options = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                options ??= new PdfCompressionOptions();

                if (!File.Exists(inputPath))
                {
                    return new PdfOperationResult { Success = false, ErrorMessage = "Input file not found" };
                }

                var originalSize = new FileInfo(inputPath).Length;

                using var inputDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
                using var outputDoc = new PdfDocument();

                // Copy pages
                for (int i = 0; i < inputDoc.PageCount; i++)
                {
                    outputDoc.AddPage(inputDoc.Pages[i]);
                }

                // Set compression options
                outputDoc.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
                outputDoc.Options.UseFlateDecoderForJpegImages = PdfUseFlateDecoderForJpegImages.Automatic;

                if (!options.RemoveMetadata)
                {
                    // The output is a new document, which starts with no description at all. Without this,
                    // compressing a file threw away its title, author and dates whatever the option said.
                    CopyDescription(inputDoc.Info, outputDoc.Info);
                }

                // Counted before the save: a PdfDocument reports no pages once it has been written out.
                var pagesProcessed = outputDoc.PageCount;
                outputDoc.Save(outputPath);

                var compressedSize = new FileInfo(outputPath).Length;
                var savings = originalSize - compressedSize;
                var savingsPercent = originalSize > 0 ? (savings * 100.0 / originalSize) : 0;

                return new PdfOperationResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    PagesProcessed = pagesProcessed,
                    OutputFiles = [outputPath],
                    ErrorMessage = $"Compressed from {FormatFileSize(originalSize)} to {FormatFileSize(compressedSize)} ({savingsPercent:F1}% reduction)"
                };
            }
            catch (Exception ex)
            {
                return new PdfOperationResult { Success = false, ErrorMessage = ex.Message };
            }
        });
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB"];
        int order = 0;
        double size = bytes;
        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }
        return $"{size:F1} {sizes[order]}";
    }

    // Drawing methods for new annotation types
    private void DrawStamp(XGraphics gfx, StampAnnotation stamp)
    {
        var stampText = stamp.StampType == StampType.Custom
            ? stamp.CustomText
            : GetStampText(stamp.StampType);

        var stampColor = GetStampColor(stamp.StampType);
        var color = XColor.FromArgb((int)(stamp.Opacity * 255), stampColor.R, stampColor.G, stampColor.B);

        // Save graphics state for rotation
        var state = gfx.Save();

        // Move origin to stamp center and rotate
        var centerX = stamp.X + stamp.Width / 2;
        var centerY = stamp.Y + stamp.Height / 2;
        gfx.TranslateTransform(centerX, centerY);
        gfx.RotateTransform(stamp.Rotation);
        gfx.TranslateTransform(-centerX, -centerY);

        // Draw stamp border
        if (stamp.ShowBorder)
        {
            var pen = new XPen(color, 3) { DashStyle = XDashStyle.Solid };
            gfx.DrawRectangle(pen, stamp.X, stamp.Y, stamp.Width, stamp.Height);

            // Double border effect
            var innerPen = new XPen(color, 1);
            gfx.DrawRectangle(innerPen, stamp.X + 4, stamp.Y + 4, stamp.Width - 8, stamp.Height - 8);
        }

        // Draw stamp text
        var fontSize = Math.Min(stamp.Width / stampText.Length * 1.5, stamp.Height * 0.4);
        var font = new XFont("Arial", fontSize, XFontStyleEx.Bold);
        var brush = new XSolidBrush(color);

        var textSize = gfx.MeasureString(stampText, font);
        var textX = stamp.X + (stamp.Width - textSize.Width) / 2;
        var textY = stamp.Y + (stamp.Height + textSize.Height) / 2 - (stamp.ShowDate ? 10 : 0);

        gfx.DrawString(stampText, font, brush, textX, textY);

        // Draw date if enabled
        if (stamp.ShowDate)
        {
            var dateFont = new XFont("Arial", fontSize * 0.4, XFontStyleEx.Regular);
            var dateText = DateTime.Now.ToString("MM/dd/yyyy");
            var dateSize = gfx.MeasureString(dateText, dateFont);
            var dateX = stamp.X + (stamp.Width - dateSize.Width) / 2;
            var dateY = textY + 15;
            gfx.DrawString(dateText, dateFont, brush, dateX, dateY);
        }

        gfx.Restore(state);
    }

    /// <param name="pageSize">The page as displayed (original rotation, visible area), in the watermark's units.</param>
    private void DrawWatermark(XGraphics gfx, WatermarkAnnotation watermark, XSize pageSize)
    {
        if (watermark.Type == WatermarkType.Image && watermark.ImageData != null)
        {
            try
            {
                using var ms = new MemoryStream(watermark.ImageData);
                var image = XImage.FromStream(ms);

                // Position based on setting
                var (x, y) = GetWatermarkPosition(watermark.Position, pageSize.Width, pageSize.Height, watermark.Width, watermark.Height);

                var state = gfx.Save();
                gfx.TranslateTransform(x + watermark.Width / 2, y + watermark.Height / 2);
                gfx.RotateTransform(watermark.Rotation);
                gfx.TranslateTransform(-watermark.Width / 2, -watermark.Height / 2);

                // Note: PdfSharp doesn't support opacity for images directly
                gfx.DrawImage(image, 0, 0, watermark.Width, watermark.Height);
                gfx.Restore(state);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "DrawWatermark failed");
            }
        }
        else
        {
            // Text watermark
            var color = ParseColor(watermark.Color);
            var watermarkColor = XColor.FromArgb((int)(watermark.Opacity * 255), color.R, color.G, color.B);

            var font = new XFont("Arial", watermark.FontSize, XFontStyleEx.Bold);
            var brush = new XSolidBrush(watermarkColor);

            var textSize = gfx.MeasureString(watermark.Text, font);
            var (x, y) = GetWatermarkPosition(watermark.Position, pageSize.Width, pageSize.Height, textSize.Width, textSize.Height);

            var state = gfx.Save();

            // Lay the text out by its box rather than its baseline, so a centred watermark is really centred.
            if (watermark.Position == WatermarkPosition.Diagonal || watermark.Position == WatermarkPosition.Center)
            {
                gfx.TranslateTransform(pageSize.Width / 2, pageSize.Height / 2);
                gfx.RotateTransform(watermark.Rotation);
                gfx.DrawString(watermark.Text, font, brush,
                    new XRect(-textSize.Width / 2, -textSize.Height / 2, textSize.Width, textSize.Height),
                    XStringFormats.Center);
            }
            else
            {
                gfx.DrawString(watermark.Text, font, brush,
                    new XRect(x, y, textSize.Width, textSize.Height), XStringFormats.TopLeft);
            }

            gfx.Restore(state);
        }
    }

    private static (double x, double y) GetWatermarkPosition(WatermarkPosition position, double pageWidth, double pageHeight, double contentWidth, double contentHeight)
    {
        return position switch
        {
            WatermarkPosition.TopLeft => (20, 20),
            WatermarkPosition.TopRight => (pageWidth - contentWidth - 20, 20),
            WatermarkPosition.BottomLeft => (20, pageHeight - contentHeight - 20),
            WatermarkPosition.BottomRight => (pageWidth - contentWidth - 20, pageHeight - contentHeight - 20),
            WatermarkPosition.Center or WatermarkPosition.Diagonal => ((pageWidth - contentWidth) / 2, (pageHeight - contentHeight) / 2),
            _ => ((pageWidth - contentWidth) / 2, (pageHeight - contentHeight) / 2)
        };
    }

    private static string GetStampText(StampType type)
    {
        return type switch
        {
            StampType.Approved => "APPROVED",
            StampType.Rejected => "REJECTED",
            StampType.Confidential => "CONFIDENTIAL",
            StampType.Draft => "DRAFT",
            StampType.Final => "FINAL",
            StampType.Void => "VOID",
            StampType.Copy => "COPY",
            StampType.Original => "ORIGINAL",
            StampType.NotApproved => "NOT APPROVED",
            StampType.ForReview => "FOR REVIEW",
            StampType.Urgent => "URGENT",
            StampType.Completed => "COMPLETED",
            StampType.Pending => "PENDING",
            _ => "STAMP"
        };
    }

    private static XColor GetStampColor(StampType type)
    {
        return type switch
        {
            StampType.Approved or StampType.Completed or StampType.Final => XColor.FromArgb(0, 128, 0),      // Green
            StampType.Rejected or StampType.Void or StampType.NotApproved => XColor.FromArgb(200, 0, 0),    // Red
            StampType.Confidential or StampType.Urgent => XColor.FromArgb(200, 0, 0),                        // Red
            StampType.Draft or StampType.ForReview or StampType.Pending => XColor.FromArgb(200, 150, 0),    // Orange/Yellow
            StampType.Copy or StampType.Original => XColor.FromArgb(0, 0, 180),                              // Blue
            _ => XColor.FromArgb(128, 128, 128)                                                               // Gray
        };
    }

    private void DrawLink(XGraphics gfx, LinkAnnotation link)
    {
        // Draw underlined text for the link
        var font = new XFont("Arial", 12, XFontStyleEx.Underline);
        var brush = XBrushes.Blue;

        var displayText = !string.IsNullOrEmpty(link.DisplayText) ? link.DisplayText : link.Url;
        gfx.DrawString(displayText, font, brush, link.X, link.Y);

        // Draw a subtle border to indicate clickable area
        var borderPen = new XPen(XColors.LightBlue, 0.5) { DashStyle = XDashStyle.Dot };
        gfx.DrawRectangle(borderPen, link.X - 2, link.Y - 12, link.Width + 4, link.Height + 4);
    }
}
