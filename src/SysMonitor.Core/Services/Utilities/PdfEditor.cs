using System.Drawing;
using System.Drawing.Imaging;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

namespace SysMonitor.Core.Services.Utilities;

public class PdfEditor : IPdfEditor
{
    public async Task<PdfDocument?> OpenPdfAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                using var pdfDoc = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);

                var document = new PdfDocument
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

    public async Task<PdfOperationResult> SavePdfAsync(PdfDocument document, string outputPath)
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
                using var outputDoc = new PdfSharp.Pdf.PdfDocument();

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
                // Note: Full PDF rendering requires Windows-specific libraries
                // This creates a simple preview with page info
                using var bitmap = new Bitmap(Math.Max(width, 200), Math.Max(height, 280));
                using var graphics = Graphics.FromImage(bitmap);

                graphics.Clear(Color.White);
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

                // Draw border
                using var borderPen = new Pen(Color.LightGray, 1);
                graphics.DrawRectangle(borderPen, 0, 0, bitmap.Width - 1, bitmap.Height - 1);

                // Draw page number indicator
                using var font = new Font("Segoe UI", 14, FontStyle.Bold);
                using var brush = new SolidBrush(Color.FromArgb(100, 0, 0, 0));
                var text = $"Page {pageNumber}";
                var textSize = graphics.MeasureString(text, font);
                graphics.DrawString(text, font, brush,
                    (bitmap.Width - textSize.Width) / 2,
                    (bitmap.Height - textSize.Height) / 2);

                // Draw page dimensions
                using var smallFont = new Font("Segoe UI", 10);
                var dimText = $"{(int)page.Width.Point} x {(int)page.Height.Point} pt";
                var dimSize = graphics.MeasureString(dimText, smallFont);
                graphics.DrawString(dimText, smallFont, brush,
                    (bitmap.Width - dimSize.Width) / 2,
                    (bitmap.Height - textSize.Height) / 2 + textSize.Height + 5);

                using var ms = new MemoryStream();
                bitmap.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
            catch
            {
                return null;
            }
        });
    }

    public async Task<PdfOperationResult> RotatePageAsync(PdfDocument document, int pageNumber, int degrees)
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

    public async Task<PdfOperationResult> DeletePageAsync(PdfDocument document, int pageNumber)
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

    public async Task<PdfOperationResult> ReorderPagesAsync(PdfDocument document, int[] newOrder)
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

    public async Task<PdfOperationResult> AddTextAnnotationAsync(PdfDocument document, int pageNumber, TextAnnotation annotation)
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

    public async Task<PdfOperationResult> AddHighlightAsync(PdfDocument document, int pageNumber, HighlightAnnotation highlight)
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

    public async Task<PdfOperationResult> AddShapeAsync(PdfDocument document, int pageNumber, ShapeAnnotation shape)
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

    private void DrawAnnotation(XGraphics gfx, PdfAnnotation annotation, PdfSharp.Pdf.PdfPage page)
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
