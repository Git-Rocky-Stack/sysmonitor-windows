using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

namespace SysMonitor.Core.Services.Utilities;

public class PdfTools : IPdfTools
{
    public async Task<PdfOperationResult> MergePdfsAsync(IEnumerable<string> inputPaths, string outputPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                var files = inputPaths.Where(File.Exists).ToList();
                if (files.Count < 2)
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = "Need at least 2 PDF files to merge"
                    };
                }

                using var outputDoc = new PdfDocument();
                var totalPages = 0;

                foreach (var file in files)
                {
                    if (!IsValidPdf(file)) continue;

                    using var inputDoc = PdfReader.Open(file, PdfDocumentOpenMode.Import);
                    for (int i = 0; i < inputDoc.PageCount; i++)
                    {
                        outputDoc.AddPage(inputDoc.Pages[i]);
                        totalPages++;
                    }
                }

                outputDoc.Save(outputPath);

                return new PdfOperationResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    PagesProcessed = totalPages,
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

    public async Task<PdfOperationResult> SplitPdfAsync(string inputPath, string outputDirectory, SplitOptions options)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(inputPath) || !IsValidPdf(inputPath))
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid PDF file"
                    };
                }

                using var inputDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);
                if (inputDoc.PageCount == 0)
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = "PDF has no pages"
                    };
                }

                if (!Directory.Exists(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                var baseName = Path.GetFileNameWithoutExtension(inputPath);
                var outputFiles = new List<string>();

                if (options.SplitAllPages)
                {
                    // Split into individual pages
                    for (int i = 0; i < inputDoc.PageCount; i++)
                    {
                        using var pageDoc = new PdfDocument();
                        pageDoc.AddPage(inputDoc.Pages[i]);
                        var outPath = Path.Combine(outputDirectory, $"{baseName}_page_{i + 1}.pdf");
                        pageDoc.Save(outPath);
                        outputFiles.Add(outPath);
                    }
                }
                else
                {
                    // Split by range
                    var startPage = Math.Max(1, options.StartPage) - 1; // Convert to 0-based
                    var endPage = Math.Min(inputDoc.PageCount, options.EndPage);

                    using var rangeDoc = new PdfDocument();
                    for (int i = startPage; i < endPage; i++)
                    {
                        rangeDoc.AddPage(inputDoc.Pages[i]);
                    }

                    var outPath = Path.Combine(outputDirectory, $"{baseName}_pages_{startPage + 1}-{endPage}.pdf");
                    rangeDoc.Save(outPath);
                    outputFiles.Add(outPath);
                }

                return new PdfOperationResult
                {
                    Success = true,
                    OutputPath = outputDirectory,
                    PagesProcessed = outputFiles.Count,
                    OutputFiles = outputFiles
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

    public async Task<PdfOperationResult> ExtractPagesAsync(string inputPath, string outputPath, int startPage, int endPage)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(inputPath) || !IsValidPdf(inputPath))
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid PDF file"
                    };
                }

                using var inputDoc = PdfReader.Open(inputPath, PdfDocumentOpenMode.Import);

                if (startPage < 1 || endPage > inputDoc.PageCount || startPage > endPage)
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = $"Invalid page range. PDF has {inputDoc.PageCount} pages."
                    };
                }

                using var outputDoc = new PdfDocument();
                for (int i = startPage - 1; i < endPage; i++)
                {
                    outputDoc.AddPage(inputDoc.Pages[i]);
                }

                outputDoc.Save(outputPath);

                return new PdfOperationResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    PagesProcessed = endPage - startPage + 1,
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

    public async Task<PdfOperationResult> AddSignatureAsync(string inputPath, string outputPath, SignatureOptions options)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(inputPath) || !IsValidPdf(inputPath))
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = "Invalid PDF file"
                    };
                }

                using var document = PdfReader.Open(inputPath, PdfDocumentOpenMode.Modify);

                if (options.PageNumber < 1 || options.PageNumber > document.PageCount)
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = $"Invalid page number. PDF has {document.PageCount} pages."
                    };
                }

                var page = document.Pages[options.PageNumber - 1];
                using var gfx = XGraphics.FromPdfPage(page);

                // Draw signature image
                if (options.SignatureImageBytes != null && options.SignatureImageBytes.Length > 0)
                {
                    using var ms = new MemoryStream(options.SignatureImageBytes);
                    var image = XImage.FromStream(ms);

                    // Calculate position (convert from percentage to points)
                    var x = page.Width.Point * (options.X / 100.0);
                    var y = page.Height.Point * (options.Y / 100.0);

                    // Scale signature to desired width while maintaining aspect ratio
                    var targetWidth = options.Width > 0 ? options.Width : 150;
                    var scale = targetWidth / image.PixelWidth;
                    var targetHeight = image.PixelHeight * scale;

                    gfx.DrawImage(image, x, y, targetWidth, targetHeight);
                }

                // Optionally add text (name, date)
                if (!string.IsNullOrEmpty(options.SignerName))
                {
                    var font = new XFont("Arial", 10, XFontStyleEx.Regular);
                    var textX = page.Width.Point * (options.X / 100.0);
                    var textY = page.Height.Point * (options.Y / 100.0) + (options.Width > 0 ? options.Width * 0.5 : 75) + 15;

                    gfx.DrawString(options.SignerName, font, XBrushes.Black, textX, textY);

                    if (options.IncludeDate)
                    {
                        var dateStr = DateTime.Now.ToString("yyyy-MM-dd");
                        gfx.DrawString($"Date: {dateStr}", font, XBrushes.Black, textX, textY + 12);
                    }
                }

                document.Save(outputPath);

                return new PdfOperationResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    PagesProcessed = 1,
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

    public async Task<PdfInfo?> GetPdfInfoAsync(string filePath)
    {
        return await Task.Run(() => GetPdfInfoSync(filePath));
    }

    public async Task<PdfOperationResult> ConvertToPdfAsync(string inputPath, string outputPath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = "Input file not found"
                    };
                }

                var extension = Path.GetExtension(inputPath).ToLowerInvariant();

                // Handle Office documents via COM automation
                if (IsWordFile(extension))
                {
                    return ConvertWordToPdf(inputPath, outputPath);
                }
                else if (IsExcelFile(extension))
                {
                    return ConvertExcelToPdf(inputPath, outputPath);
                }
                else if (IsPowerPointFile(extension))
                {
                    return ConvertPowerPointToPdf(inputPath, outputPath);
                }

                using var document = new PdfDocument();

                if (IsImageFile(extension))
                {
                    // Convert image to PDF
                    var page = document.AddPage();

                    using var gfx = XGraphics.FromPdfPage(page);
                    using var image = XImage.FromFile(inputPath);

                    // Calculate scaling to fit the page while maintaining aspect ratio
                    var pageWidth = page.Width.Point;
                    var pageHeight = page.Height.Point;

                    var imageWidth = image.PixelWidth;
                    var imageHeight = image.PixelHeight;

                    // Calculate scale to fit within page with margins
                    const double margin = 36; // 0.5 inch margins
                    var availableWidth = pageWidth - (margin * 2);
                    var availableHeight = pageHeight - (margin * 2);

                    var scaleX = availableWidth / imageWidth;
                    var scaleY = availableHeight / imageHeight;
                    var scale = Math.Min(scaleX, scaleY);

                    var scaledWidth = imageWidth * scale;
                    var scaledHeight = imageHeight * scale;

                    // Center the image on the page
                    var x = (pageWidth - scaledWidth) / 2;
                    var y = (pageHeight - scaledHeight) / 2;

                    gfx.DrawImage(image, x, y, scaledWidth, scaledHeight);
                }
                else if (extension == ".txt" || extension == ".csv" || extension == ".xml" ||
                         extension == ".json" || extension == ".md" || extension == ".log")
                {
                    // Convert text-based files to PDF
                    var text = File.ReadAllText(inputPath);
                    var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

                    var font = new XFont("Consolas", 10, XFontStyleEx.Regular);
                    const double margin = 50;
                    const double lineHeight = 14;

                    var page = document.AddPage();
                    var gfx = XGraphics.FromPdfPage(page);
                    var y = margin;
                    var maxWidth = page.Width.Point - (margin * 2);

                    foreach (var line in lines)
                    {
                        if (y > page.Height.Point - margin)
                        {
                            // Add new page
                            gfx.Dispose();
                            page = document.AddPage();
                            gfx = XGraphics.FromPdfPage(page);
                            y = margin;
                        }

                        // Truncate long lines
                        var displayLine = line;
                        if (!string.IsNullOrEmpty(line))
                        {
                            var size = gfx.MeasureString(line, font);
                            if (size.Width > maxWidth)
                            {
                                // Truncate to fit
                                var ratio = maxWidth / size.Width;
                                var chars = (int)(line.Length * ratio) - 3;
                                if (chars > 0)
                                    displayLine = line.Substring(0, chars) + "...";
                            }
                        }

                        gfx.DrawString(displayLine, font, XBrushes.Black, margin, y);
                        y += lineHeight;
                    }
                    gfx.Dispose();
                }
                else
                {
                    return new PdfOperationResult
                    {
                        Success = false,
                        ErrorMessage = $"Unsupported file format: {extension}"
                    };
                }

                document.Save(outputPath);

                return new PdfOperationResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    PagesProcessed = document.PageCount,
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

    private static PdfOperationResult ConvertWordToPdf(string inputPath, string outputPath)
    {
        dynamic? wordApp = null;
        dynamic? doc = null;

        try
        {
            var wordType = Type.GetTypeFromProgID("Word.Application");
            if (wordType == null)
            {
                return new PdfOperationResult
                {
                    Success = false,
                    ErrorMessage = "Microsoft Word is not installed. Please install Microsoft Office to convert Word documents."
                };
            }

            wordApp = Activator.CreateInstance(wordType);
            if (wordApp == null)
            {
                return new PdfOperationResult
                {
                    Success = false,
                    ErrorMessage = "Failed to create Word application instance."
                };
            }
            wordApp.Visible = false;
            wordApp.DisplayAlerts = 0; // wdAlertsNone

            doc = wordApp.Documents.Open(inputPath, ReadOnly: true);
            doc.SaveAs2(outputPath, 17); // 17 = wdFormatPDF

            return new PdfOperationResult
            {
                Success = true,
                OutputPath = outputPath,
                PagesProcessed = 1,
                OutputFiles = [outputPath]
            };
        }
        catch (COMException ex)
        {
            return new PdfOperationResult
            {
                Success = false,
                ErrorMessage = $"Word conversion failed: {ex.Message}. Ensure Microsoft Word is installed."
            };
        }
        catch (Exception ex)
        {
            return new PdfOperationResult
            {
                Success = false,
                ErrorMessage = $"Conversion failed: {ex.Message}"
            };
        }
        finally
        {
            try
            {
                doc?.Close(false);
                wordApp?.Quit();
                if (doc != null) Marshal.ReleaseComObject(doc);
                if (wordApp != null) Marshal.ReleaseComObject(wordApp);
            }
            catch { }
        }
    }

    private static PdfOperationResult ConvertExcelToPdf(string inputPath, string outputPath)
    {
        dynamic? excelApp = null;
        dynamic? workbook = null;

        try
        {
            var excelType = Type.GetTypeFromProgID("Excel.Application");
            if (excelType == null)
            {
                return new PdfOperationResult
                {
                    Success = false,
                    ErrorMessage = "Microsoft Excel is not installed. Please install Microsoft Office to convert Excel documents."
                };
            }

            excelApp = Activator.CreateInstance(excelType);
            if (excelApp == null)
            {
                return new PdfOperationResult
                {
                    Success = false,
                    ErrorMessage = "Failed to create Excel application instance."
                };
            }
            excelApp.Visible = false;
            excelApp.DisplayAlerts = false;

            workbook = excelApp.Workbooks.Open(inputPath, ReadOnly: true);
            workbook.ExportAsFixedFormat(0, outputPath); // 0 = xlTypePDF

            return new PdfOperationResult
            {
                Success = true,
                OutputPath = outputPath,
                PagesProcessed = 1,
                OutputFiles = [outputPath]
            };
        }
        catch (COMException ex)
        {
            return new PdfOperationResult
            {
                Success = false,
                ErrorMessage = $"Excel conversion failed: {ex.Message}. Ensure Microsoft Excel is installed."
            };
        }
        catch (Exception ex)
        {
            return new PdfOperationResult
            {
                Success = false,
                ErrorMessage = $"Conversion failed: {ex.Message}"
            };
        }
        finally
        {
            try
            {
                workbook?.Close(false);
                excelApp?.Quit();
                if (workbook != null) Marshal.ReleaseComObject(workbook);
                if (excelApp != null) Marshal.ReleaseComObject(excelApp);
            }
            catch { }
        }
    }

    private static PdfOperationResult ConvertPowerPointToPdf(string inputPath, string outputPath)
    {
        dynamic? pptApp = null;
        dynamic? presentation = null;

        try
        {
            var pptType = Type.GetTypeFromProgID("PowerPoint.Application");
            if (pptType == null)
            {
                return new PdfOperationResult
                {
                    Success = false,
                    ErrorMessage = "Microsoft PowerPoint is not installed. Please install Microsoft Office to convert PowerPoint documents."
                };
            }

            pptApp = Activator.CreateInstance(pptType);
            if (pptApp == null)
            {
                return new PdfOperationResult
                {
                    Success = false,
                    ErrorMessage = "Failed to create PowerPoint application instance."
                };
            }
            // PowerPoint must be visible to work properly in some cases
            // pptApp.Visible = true; // Commented out to keep it hidden if possible

            presentation = pptApp.Presentations.Open(inputPath, ReadOnly: true, Untitled: false, WithWindow: false);
            presentation.SaveAs(outputPath, 32); // 32 = ppSaveAsPDF

            return new PdfOperationResult
            {
                Success = true,
                OutputPath = outputPath,
                PagesProcessed = 1,
                OutputFiles = [outputPath]
            };
        }
        catch (COMException ex)
        {
            return new PdfOperationResult
            {
                Success = false,
                ErrorMessage = $"PowerPoint conversion failed: {ex.Message}. Ensure Microsoft PowerPoint is installed."
            };
        }
        catch (Exception ex)
        {
            return new PdfOperationResult
            {
                Success = false,
                ErrorMessage = $"Conversion failed: {ex.Message}"
            };
        }
        finally
        {
            try
            {
                presentation?.Close();
                pptApp?.Quit();
                if (presentation != null) Marshal.ReleaseComObject(presentation);
                if (pptApp != null) Marshal.ReleaseComObject(pptApp);
            }
            catch { }
        }
    }

    public bool IsValidPdf(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            using var stream = File.OpenRead(filePath);
            var header = new byte[8];
            stream.Read(header, 0, 8);

            // PDF files start with %PDF-
            return header[0] == 0x25 && // %
                   header[1] == 0x50 && // P
                   header[2] == 0x44 && // D
                   header[3] == 0x46 && // F
                   header[4] == 0x2D;   // -
        }
        catch
        {
            return false;
        }
    }

    public bool IsSupportedForConversion(string filePath)
    {
        if (!File.Exists(filePath))
            return false;

        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return IsImageFile(extension) || IsTextFile(extension) ||
               IsWordFile(extension) || IsExcelFile(extension) || IsPowerPointFile(extension);
    }

    private static bool IsImageFile(string extension)
    {
        return extension switch
        {
            ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".tiff" or ".tif" or ".webp" or ".ico" => true,
            _ => false
        };
    }

    private static bool IsTextFile(string extension)
    {
        return extension switch
        {
            ".txt" or ".csv" or ".xml" or ".json" or ".md" or ".log" or ".rtf" or ".html" or ".htm" => true,
            _ => false
        };
    }

    private static bool IsWordFile(string extension)
    {
        return extension switch
        {
            ".doc" or ".docx" or ".odt" or ".rtf" => true,
            _ => false
        };
    }

    private static bool IsExcelFile(string extension)
    {
        return extension switch
        {
            ".xls" or ".xlsx" or ".xlsm" or ".ods" => true,
            _ => false
        };
    }

    private static bool IsPowerPointFile(string extension)
    {
        return extension switch
        {
            ".ppt" or ".pptx" or ".odp" => true,
            _ => false
        };
    }

    private PdfInfo? GetPdfInfoSync(string filePath)
    {
        try
        {
            if (!File.Exists(filePath) || !IsValidPdf(filePath))
                return null;

            var fileInfo = new FileInfo(filePath);

            // Use PDFsharp to get accurate page count
            int pageCount;
            string pdfVersion;
            string title = "";
            string author = "";

            try
            {
                // Use Import mode instead of deprecated ReadOnly
                using var doc = PdfReader.Open(filePath, PdfDocumentOpenMode.Import);
                pageCount = doc.PageCount;
                pdfVersion = doc.Version.ToString();
                title = doc.Info.Title ?? "";
                author = doc.Info.Author ?? "";
            }
            catch
            {
                // Fallback to estimation if PDFsharp can't open it
                pageCount = EstimatePageCount(filePath);
                pdfVersion = ExtractPdfVersion(filePath);
                (title, author) = ExtractBasicMetadata(filePath);
            }

            return new PdfInfo
            {
                FileName = fileInfo.Name,
                FilePath = filePath,
                PageCount = pageCount,
                FileSizeBytes = fileInfo.Length,
                FormattedSize = FormatSize(fileInfo.Length),
                PdfVersion = pdfVersion,
                Title = title,
                Author = author,
                CreatedDate = fileInfo.CreationTime,
                ModifiedDate = fileInfo.LastWriteTime,
                IsEncrypted = false
            };
        }
        catch
        {
            return null;
        }
    }

    private static int EstimatePageCount(string filePath)
    {
        try
        {
            const int bufferSize = 64 * 1024;
            const string searchPattern = "/Type /Page";
            var count = 0;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize);
            using var reader = new StreamReader(stream, Encoding.Latin1, detectEncodingFromByteOrderMarks: false, bufferSize);

            var buffer = new char[bufferSize];
            int bytesRead;

            while ((bytesRead = reader.Read(buffer, 0, bufferSize)) > 0)
            {
                var chunk = new string(buffer, 0, bytesRead);
                var index = 0;
                while ((index = chunk.IndexOf(searchPattern, index, StringComparison.Ordinal)) != -1)
                {
                    var nextCharPos = index + searchPattern.Length;
                    var nextChar = nextCharPos < chunk.Length ? chunk[nextCharPos] : ' ';
                    if (nextChar != 's' && nextChar != 'S')
                    {
                        count++;
                    }
                    index++;
                }
            }

            return Math.Max(count, 1);
        }
        catch
        {
            return 1;
        }
    }

    private static (string title, string author) ExtractBasicMetadata(string filePath)
    {
        try
        {
            const int chunkSize = 32 * 1024;
            var title = "";
            var author = "";

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var buffer = new byte[chunkSize];

            var bytesRead = stream.Read(buffer, 0, chunkSize);
            if (bytesRead > 0)
            {
                var content = Encoding.Latin1.GetString(buffer, 0, bytesRead);
                title = ExtractField(content, "/Title");
                author = ExtractField(content, "/Author");
            }

            return (title, author);
        }
        catch
        {
            return ("", "");
        }
    }

    private static string ExtractField(string content, string field)
    {
        try
        {
            var index = content.IndexOf(field, StringComparison.Ordinal);
            if (index == -1) return "";

            var start = content.IndexOf('(', index);
            if (start == -1 || start > index + 50) return "";

            var end = content.IndexOf(')', start);
            if (end == -1) return "";

            return content.Substring(start + 1, end - start - 1);
        }
        catch
        {
            return "";
        }
    }

    private static string ExtractPdfVersion(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var header = new byte[10];
            stream.Read(header, 0, 10);

            var headerStr = Encoding.ASCII.GetString(header);
            if (headerStr.StartsWith("%PDF-"))
            {
                return headerStr.Substring(5, 3).Trim();
            }
        }
        catch { }
        return "";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_073_741_824)
            return $"{bytes / 1_073_741_824.0:F2} GB";
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:F2} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} B";
    }
}
