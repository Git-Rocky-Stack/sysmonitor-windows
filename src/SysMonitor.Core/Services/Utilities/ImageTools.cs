using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace SysMonitor.Core.Services.Utilities;

public class ImageTools : IImageTools
{
    private static readonly Dictionary<ImageFormat, System.Drawing.Imaging.ImageFormat> FormatMap = new()
    {
        { ImageFormat.Jpeg, System.Drawing.Imaging.ImageFormat.Jpeg },
        { ImageFormat.Png, System.Drawing.Imaging.ImageFormat.Png },
        { ImageFormat.Bmp, System.Drawing.Imaging.ImageFormat.Bmp },
        { ImageFormat.Gif, System.Drawing.Imaging.ImageFormat.Gif },
        { ImageFormat.Ico, System.Drawing.Imaging.ImageFormat.Icon }
    };

    private static readonly Dictionary<ImageFormat, string> ExtensionMap = new()
    {
        { ImageFormat.Jpeg, ".jpg" },
        { ImageFormat.Png, ".png" },
        { ImageFormat.Bmp, ".bmp" },
        { ImageFormat.Gif, ".gif" },
        { ImageFormat.WebP, ".webp" },
        { ImageFormat.Ico, ".ico" }
    };

    public string[] SupportedFormats => [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".ico"];

    public bool IsValidImage(string filePath)
    {
        if (!File.Exists(filePath)) return false;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        if (!SupportedFormats.Contains(ext)) return false;

        try
        {
            using var img = Image.FromFile(filePath);
            return img.Width > 0 && img.Height > 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ImageInfo?> GetImageInfoAsync(string filePath)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                var fileInfo = new FileInfo(filePath);

                using var img = Image.FromFile(filePath);
                var format = GetFormatName(img.RawFormat);
                var hasAlpha = Image.IsAlphaPixelFormat(img.PixelFormat);
                var colorDepth = Image.GetPixelFormatSize(img.PixelFormat);

                // Try to get DPI
                var dpiX = img.HorizontalResolution;
                var dpiY = img.VerticalResolution;

                return new ImageInfo
                {
                    FileName = fileInfo.Name,
                    FilePath = filePath,
                    Width = img.Width,
                    Height = img.Height,
                    FileSizeBytes = fileInfo.Length,
                    FormattedSize = FormatSize(fileInfo.Length),
                    Format = format,
                    ColorDepth = $"{colorDepth}-bit",
                    HasAlpha = hasAlpha,
                    CreatedDate = fileInfo.CreationTime,
                    ModifiedDate = fileInfo.LastWriteTime,
                    Resolution = $"{dpiX:F0} × {dpiY:F0} DPI"
                };
            }
            catch
            {
                return null;
            }
        });
    }

    public async Task<ImageOperationResult> CompressImageAsync(string inputPath, int quality = 80, IProgress<int>? progress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    return new ImageOperationResult { Success = false, ErrorMessage = "File not found" };
                }

                progress?.Report(10);

                var originalSize = new FileInfo(inputPath).Length;
                var directory = Path.GetDirectoryName(inputPath) ?? "";
                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var ext = Path.GetExtension(inputPath).ToLowerInvariant();
                var outputPath = Path.Combine(directory, $"{fileName}_compressed{ext}");

                progress?.Report(25);

                using var img = Image.FromFile(inputPath);

                progress?.Report(50);

                // Create encoder parameters for quality
                using var encoderParams = new EncoderParameters(1);
                encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);

                // Determine output format
                var codec = GetEncoderInfo(img.RawFormat) ?? GetEncoderInfo(System.Drawing.Imaging.ImageFormat.Jpeg);
                if (codec == null)
                {
                    return new ImageOperationResult { Success = false, ErrorMessage = "No suitable encoder found" };
                }

                progress?.Report(75);

                img.Save(outputPath, codec, encoderParams);
                encoderParams.Param[0].Dispose();

                progress?.Report(100);

                var newSize = new FileInfo(outputPath).Length;
                var ratio = originalSize > 0 ? (1 - (double)newSize / originalSize) * 100 : 0;

                return new ImageOperationResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    OriginalSize = originalSize,
                    NewSize = newSize,
                    CompressionRatio = Math.Round(ratio, 1),
                    OutputFiles = [outputPath]
                };
            }
            catch (Exception ex)
            {
                return new ImageOperationResult { Success = false, ErrorMessage = ex.Message };
            }
        });
    }

    public async Task<ImageOperationResult> ConvertImageAsync(string inputPath, ImageFormat targetFormat, int quality = 90, IProgress<int>? progress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    return new ImageOperationResult { Success = false, ErrorMessage = "File not found" };
                }

                progress?.Report(10);

                var originalSize = new FileInfo(inputPath).Length;
                var directory = Path.GetDirectoryName(inputPath) ?? "";
                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var newExt = ExtensionMap.TryGetValue(targetFormat, out var ext) ? ext : ".jpg";
                var outputPath = Path.Combine(directory, $"{fileName}{newExt}");

                // Avoid overwriting same file
                if (File.Exists(outputPath) && outputPath.Equals(inputPath, StringComparison.OrdinalIgnoreCase))
                {
                    outputPath = Path.Combine(directory, $"{fileName}_converted{newExt}");
                }

                progress?.Report(25);

                using var img = Image.FromFile(inputPath);

                progress?.Report(50);

                if (targetFormat == ImageFormat.WebP)
                {
                    // WebP not natively supported by System.Drawing, save as PNG instead
                    img.Save(outputPath.Replace(".webp", ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    outputPath = outputPath.Replace(".webp", ".png");
                }
                else if (FormatMap.TryGetValue(targetFormat, out var format))
                {
                    if (targetFormat == ImageFormat.Jpeg)
                    {
                        // Use quality parameter for JPEG
                        using var encoderParams = new EncoderParameters(1);
                        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
                        var codec = GetEncoderInfo(System.Drawing.Imaging.ImageFormat.Jpeg);
                        if (codec != null)
                        {
                            img.Save(outputPath, codec, encoderParams);
                        }
                        else
                        {
                            img.Save(outputPath, format);
                        }
                        encoderParams.Param[0].Dispose();
                    }
                    else
                    {
                        img.Save(outputPath, format);
                    }
                }
                else
                {
                    return new ImageOperationResult { Success = false, ErrorMessage = "Unsupported format" };
                }

                progress?.Report(100);

                var newSize = new FileInfo(outputPath).Length;
                var ratio = originalSize > 0 ? (1 - (double)newSize / originalSize) * 100 : 0;

                return new ImageOperationResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    OriginalSize = originalSize,
                    NewSize = newSize,
                    CompressionRatio = Math.Round(ratio, 1),
                    OutputFiles = [outputPath]
                };
            }
            catch (Exception ex)
            {
                return new ImageOperationResult { Success = false, ErrorMessage = ex.Message };
            }
        });
    }

    public async Task<ImageOperationResult> ResizeImageAsync(string inputPath, int maxWidth, int maxHeight, bool maintainAspectRatio = true, IProgress<int>? progress = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(inputPath))
                {
                    return new ImageOperationResult { Success = false, ErrorMessage = "File not found" };
                }

                progress?.Report(10);

                var originalSize = new FileInfo(inputPath).Length;
                var directory = Path.GetDirectoryName(inputPath) ?? "";
                var fileName = Path.GetFileNameWithoutExtension(inputPath);
                var ext = Path.GetExtension(inputPath);
                var outputPath = Path.Combine(directory, $"{fileName}_resized{ext}");

                progress?.Report(25);

                using var img = Image.FromFile(inputPath);

                progress?.Report(50);

                int newWidth, newHeight;

                if (maintainAspectRatio)
                {
                    var ratioX = (double)maxWidth / img.Width;
                    var ratioY = (double)maxHeight / img.Height;
                    var ratio = Math.Min(ratioX, ratioY);

                    newWidth = (int)(img.Width * ratio);
                    newHeight = (int)(img.Height * ratio);
                }
                else
                {
                    newWidth = maxWidth;
                    newHeight = maxHeight;
                }

                // Don't upscale if image is smaller
                if (newWidth >= img.Width && newHeight >= img.Height)
                {
                    newWidth = img.Width;
                    newHeight = img.Height;
                }

                using var resized = new Bitmap(newWidth, newHeight);
                using var graphics = Graphics.FromImage(resized);

                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = CompositingQuality.HighQuality;

                graphics.DrawImage(img, 0, 0, newWidth, newHeight);

                progress?.Report(75);

                // Save in same format
                var format = img.RawFormat;
                var codec = GetEncoderInfo(format);
                if (codec != null)
                {
                    using var encoderParams = new EncoderParameters(1);
                    encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, 90L);
                    resized.Save(outputPath, codec, encoderParams);
                    encoderParams.Param[0].Dispose();
                }
                else
                {
                    resized.Save(outputPath, format);
                }

                progress?.Report(100);

                var newSize = new FileInfo(outputPath).Length;
                var compressionRatio = originalSize > 0 ? (1 - (double)newSize / originalSize) * 100 : 0;

                return new ImageOperationResult
                {
                    Success = true,
                    OutputPath = outputPath,
                    OriginalSize = originalSize,
                    NewSize = newSize,
                    CompressionRatio = Math.Round(compressionRatio, 1),
                    OutputFiles = [outputPath]
                };
            }
            catch (Exception ex)
            {
                return new ImageOperationResult { Success = false, ErrorMessage = ex.Message };
            }
        });
    }

    public async Task<ImageOperationResult> BatchConvertAsync(IEnumerable<string> inputPaths, ImageFormat targetFormat, int quality = 90, IProgress<BatchImageProgress>? progress = null)
    {
        var paths = inputPaths.ToList();
        var outputFiles = new List<string>();
        var totalOriginalSize = 0L;
        var totalNewSize = 0L;
        var processed = 0;
        var errors = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                progress?.Report(new BatchImageProgress
                {
                    ProcessedCount = processed,
                    TotalCount = paths.Count,
                    CurrentFile = Path.GetFileName(path),
                    Status = $"Converting {Path.GetFileName(path)}..."
                });

                var result = await ConvertImageAsync(path, targetFormat, quality);

                if (result.Success)
                {
                    outputFiles.Add(result.OutputPath);
                    totalOriginalSize += result.OriginalSize;
                    totalNewSize += result.NewSize;
                }
                else
                {
                    errors.Add($"{Path.GetFileName(path)}: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }

            processed++;
        }

        progress?.Report(new BatchImageProgress
        {
            ProcessedCount = processed,
            TotalCount = paths.Count,
            CurrentFile = "",
            Status = "Complete"
        });

        var compressionRatio = totalOriginalSize > 0 ? (1 - (double)totalNewSize / totalOriginalSize) * 100 : 0;

        return new ImageOperationResult
        {
            Success = errors.Count == 0,
            OutputPath = outputFiles.FirstOrDefault() ?? "",
            OriginalSize = totalOriginalSize,
            NewSize = totalNewSize,
            CompressionRatio = Math.Round(compressionRatio, 1),
            ErrorMessage = errors.Count > 0 ? string.Join("; ", errors) : "",
            OutputFiles = outputFiles
        };
    }

    public async Task<ImageOperationResult> BatchResizeAsync(IEnumerable<string> inputPaths, int maxWidth, int maxHeight, bool maintainAspectRatio = true, IProgress<BatchImageProgress>? progress = null)
    {
        var paths = inputPaths.ToList();
        var outputFiles = new List<string>();
        var totalOriginalSize = 0L;
        var totalNewSize = 0L;
        var processed = 0;
        var errors = new List<string>();

        foreach (var path in paths)
        {
            try
            {
                progress?.Report(new BatchImageProgress
                {
                    ProcessedCount = processed,
                    TotalCount = paths.Count,
                    CurrentFile = Path.GetFileName(path),
                    Status = $"Resizing {Path.GetFileName(path)}..."
                });

                var result = await ResizeImageAsync(path, maxWidth, maxHeight, maintainAspectRatio);

                if (result.Success)
                {
                    outputFiles.Add(result.OutputPath);
                    totalOriginalSize += result.OriginalSize;
                    totalNewSize += result.NewSize;
                }
                else
                {
                    errors.Add($"{Path.GetFileName(path)}: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }

            processed++;
        }

        progress?.Report(new BatchImageProgress
        {
            ProcessedCount = processed,
            TotalCount = paths.Count,
            CurrentFile = "",
            Status = "Complete"
        });

        var compressionRatio = totalOriginalSize > 0 ? (1 - (double)totalNewSize / totalOriginalSize) * 100 : 0;

        return new ImageOperationResult
        {
            Success = errors.Count == 0,
            OutputPath = outputFiles.FirstOrDefault() ?? "",
            OriginalSize = totalOriginalSize,
            NewSize = totalNewSize,
            CompressionRatio = Math.Round(compressionRatio, 1),
            ErrorMessage = errors.Count > 0 ? string.Join("; ", errors) : "",
            OutputFiles = outputFiles
        };
    }

    private static ImageCodecInfo? GetEncoderInfo(System.Drawing.Imaging.ImageFormat format)
    {
        return ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.FormatID == format.Guid);
    }

    private static string GetFormatName(System.Drawing.Imaging.ImageFormat format)
    {
        if (format.Guid == System.Drawing.Imaging.ImageFormat.Jpeg.Guid) return "JPEG";
        if (format.Guid == System.Drawing.Imaging.ImageFormat.Png.Guid) return "PNG";
        if (format.Guid == System.Drawing.Imaging.ImageFormat.Bmp.Guid) return "BMP";
        if (format.Guid == System.Drawing.Imaging.ImageFormat.Gif.Guid) return "GIF";
        if (format.Guid == System.Drawing.Imaging.ImageFormat.Icon.Guid) return "ICO";
        if (format.Guid == System.Drawing.Imaging.ImageFormat.Tiff.Guid) return "TIFF";
        return "Unknown";
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000.0:F2} GB";
        if (bytes >= 1_000_000) return $"{bytes / 1_000_000.0:F2} MB";
        if (bytes >= 1_000) return $"{bytes / 1_000.0:F2} KB";
        return $"{bytes} B";
    }
}
