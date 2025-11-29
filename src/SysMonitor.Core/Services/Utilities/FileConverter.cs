using System.IO.Compression;

namespace SysMonitor.Core.Services.Utilities;

public class FileConverter : IFileConverter
{
    private static readonly string[] SupportedImageFormats = ["jpg", "jpeg", "png", "bmp", "gif", "webp"];

    public async Task<ConversionResult> ConvertImageAsync(string sourcePath, string targetFormat,
        string? outputPath = null, ImageConversionOptions? options = null)
    {
        // Note: Full image conversion would require System.Drawing.Common or SkiaSharp
        // This is a placeholder that copies the file - full implementation would need image library
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return new ConversionResult
                    {
                        Success = false,
                        ErrorMessage = "Source file not found"
                    };
                }

                var sourceInfo = new FileInfo(sourcePath);
                var targetPath = outputPath ?? Path.ChangeExtension(sourcePath, targetFormat);

                // For now, just copy - full implementation would need image processing library
                File.Copy(sourcePath, targetPath, true);

                var targetInfo = new FileInfo(targetPath);

                return new ConversionResult
                {
                    Success = true,
                    OutputPath = targetPath,
                    OriginalSize = sourceInfo.Length,
                    NewSize = targetInfo.Length
                };
            }
            catch (Exception ex)
            {
                return new ConversionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        });
    }

    public async Task<ConversionResult> CompressFileAsync(string sourcePath, CompressionFormat format,
        string? outputPath = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return new ConversionResult
                    {
                        Success = false,
                        ErrorMessage = "Source file not found"
                    };
                }

                var sourceInfo = new FileInfo(sourcePath);
                var extension = format switch
                {
                    CompressionFormat.Zip => ".zip",
                    CompressionFormat.GZip => ".gz",
                    CompressionFormat.SevenZip => ".7z",
                    _ => ".zip"
                };

                var targetPath = outputPath ?? sourcePath + extension;

                switch (format)
                {
                    case CompressionFormat.Zip:
                        CompressToZip(sourcePath, targetPath);
                        break;

                    case CompressionFormat.GZip:
                        CompressToGZip(sourcePath, targetPath);
                        break;

                    case CompressionFormat.SevenZip:
                        // 7z would require 7-Zip SDK or SevenZipSharp
                        // Fall back to zip for now
                        targetPath = Path.ChangeExtension(targetPath, ".zip");
                        CompressToZip(sourcePath, targetPath);
                        break;
                }

                var targetInfo = new FileInfo(targetPath);

                return new ConversionResult
                {
                    Success = true,
                    OutputPath = targetPath,
                    OriginalSize = sourceInfo.Length,
                    NewSize = targetInfo.Length
                };
            }
            catch (Exception ex)
            {
                return new ConversionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        });
    }

    public async Task<ConversionResult> DecompressFileAsync(string sourcePath, string? outputPath = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return new ConversionResult
                    {
                        Success = false,
                        ErrorMessage = "Source file not found"
                    };
                }

                var sourceInfo = new FileInfo(sourcePath);
                var extension = sourceInfo.Extension.ToLowerInvariant();

                string targetPath;

                switch (extension)
                {
                    case ".zip":
                        targetPath = outputPath ?? Path.Combine(
                            sourceInfo.DirectoryName ?? "",
                            Path.GetFileNameWithoutExtension(sourcePath));

                        if (!Directory.Exists(targetPath))
                            Directory.CreateDirectory(targetPath);

                        ZipFile.ExtractToDirectory(sourcePath, targetPath, true);
                        break;

                    case ".gz":
                        targetPath = outputPath ?? sourcePath[..^3]; // Remove .gz
                        DecompressGZip(sourcePath, targetPath);
                        break;

                    default:
                        return new ConversionResult
                        {
                            Success = false,
                            ErrorMessage = $"Unsupported archive format: {extension}"
                        };
                }

                // For directories, get total size
                long newSize = 0;
                if (Directory.Exists(targetPath))
                {
                    newSize = Directory.GetFiles(targetPath, "*", SearchOption.AllDirectories)
                        .Sum(f => new FileInfo(f).Length);
                }
                else if (File.Exists(targetPath))
                {
                    newSize = new FileInfo(targetPath).Length;
                }

                return new ConversionResult
                {
                    Success = true,
                    OutputPath = targetPath,
                    OriginalSize = sourceInfo.Length,
                    NewSize = newSize
                };
            }
            catch (Exception ex)
            {
                return new ConversionResult
                {
                    Success = false,
                    ErrorMessage = ex.Message
                };
            }
        });
    }

    public IEnumerable<string> GetSupportedImageFormats() => SupportedImageFormats;

    public IEnumerable<CompressionFormat> GetSupportedCompressionFormats() =>
        Enum.GetValues<CompressionFormat>();

    private static void CompressToZip(string sourcePath, string targetPath)
    {
        // Delete existing file if it exists
        if (File.Exists(targetPath))
            File.Delete(targetPath);

        using var archive = ZipFile.Open(targetPath, ZipArchiveMode.Create);
        var fileInfo = new FileInfo(sourcePath);

        if (fileInfo.Attributes.HasFlag(FileAttributes.Directory))
        {
            // It's a directory - zip all contents
            var files = Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var relativePath = Path.GetRelativePath(sourcePath, file);
                archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
            }
        }
        else
        {
            // Single file
            archive.CreateEntryFromFile(sourcePath, fileInfo.Name, CompressionLevel.Optimal);
        }
    }

    private static void CompressToGZip(string sourcePath, string targetPath)
    {
        using var sourceStream = File.OpenRead(sourcePath);
        using var targetStream = File.Create(targetPath);
        using var gzipStream = new GZipStream(targetStream, CompressionLevel.Optimal);
        sourceStream.CopyTo(gzipStream);
    }

    private static void DecompressGZip(string sourcePath, string targetPath)
    {
        using var sourceStream = File.OpenRead(sourcePath);
        using var gzipStream = new GZipStream(sourceStream, CompressionMode.Decompress);
        using var targetStream = File.Create(targetPath);
        gzipStream.CopyTo(targetStream);
    }
}
