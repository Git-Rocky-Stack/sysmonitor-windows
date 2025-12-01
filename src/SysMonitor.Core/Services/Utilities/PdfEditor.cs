using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

namespace SysMonitor.Core.Services.Utilities;

public class PdfEditor : IPdfEditor
{
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
                    document.Pages.Add(new PdfPageInfo
                    {
                        PageNumber = i + 1,
                        Width = page.Width.Point,
                        Height = page.Height.Point,
                        Rotation = (int)page.Rotate
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

                // Reorder and process pages based on document.Pages
                foreach (var pageInfo in document.Pages)
                {
                    if (pageInfo.PageNumber < 1 || pageInfo.PageNumber > inputDoc.PageCount)
                        continue;

                    var page = outputDoc.AddPage(inputDoc.Pages[pageInfo.PageNumber - 1]);

                    // Apply rotation
                    if (pageInfo.Rotation != 0)
                    {
                        page.Rotate = pageInfo.Rotation;
                    }

                    // Apply annotations for this page
                    var pageAnnotations = document.Annotations.Where(a => a.PageNumber == pageInfo.PageNumber).ToList();
                    if (pageAnnotations.Any())
                    {
                        using var gfx = XGraphics.FromPdfPage(page);
                        foreach (var annotation in pageAnnotations)
                        {
                            DrawAnnotation(gfx, annotation, page);
                        }
                    }
                }

                outputDoc.Save(outputPath);

                return new PdfOperationResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    PagesProcessed = outputDoc.PageCount,
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

    public async Task<PdfOperationResult> RotatePageAsync(PdfEditorDocument document, int pageNumber, int degrees)
    {
        return await Task.Run(() =>
        {
            try
            {
                var page = document.Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
                if (page == null)
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = $"Page {pageNumber} not found"
                    };
                }

                // Normalize rotation to 0, 90, 180, or 270
                page.Rotation = (page.Rotation + degrees) % 360;
                if (page.Rotation < 0) page.Rotation += 360;

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

    public async Task<PdfOperationResult> DeletePageAsync(PdfEditorDocument document, int pageNumber)
    {
        return await Task.Run(() =>
        {
            try
            {
                var page = document.Pages.FirstOrDefault(p => p.PageNumber == pageNumber);
                if (page == null)
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = $"Page {pageNumber} not found"
                    };
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
                document.Annotations.RemoveAll(a => a.PageNumber == pageNumber);

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

    public async Task<PdfOperationResult> AddTextAnnotationAsync(PdfEditorDocument document, int pageNumber, TextAnnotation annotation)
    {
        return await Task.Run(() =>
        {
            try
            {
                annotation.PageNumber = pageNumber;
                document.Annotations.Add(annotation);
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

    public async Task<PdfOperationResult> AddHighlightAsync(PdfEditorDocument document, int pageNumber, HighlightAnnotation highlight)
    {
        return await Task.Run(() =>
        {
            try
            {
                highlight.PageNumber = pageNumber;
                document.Annotations.Add(highlight);
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

    public async Task<PdfOperationResult> AddShapeAsync(PdfEditorDocument document, int pageNumber, ShapeAnnotation shape)
    {
        return await Task.Run(() =>
        {
            try
            {
                shape.PageNumber = pageNumber;
                document.Annotations.Add(shape);
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
                    pages.Add(new PdfPageInfo
                    {
                        PageNumber = i + 1,
                        Width = page.Width.Point,
                        Height = page.Height.Point,
                        Rotation = (int)page.Rotate
                    });
                }
            }
            catch { }

            return pages;
        });
    }

    private void DrawAnnotation(XGraphics gfx, PdfAnnotation annotation, PdfPage page)
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

        gfx.DrawString(annotation.Text, font, brush, annotation.X, annotation.Y);
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

    private static XColor ParseColor(string colorString)
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
        catch { }

        return XColors.Red;
    }
}
