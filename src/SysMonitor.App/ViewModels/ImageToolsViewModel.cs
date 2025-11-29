using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;

namespace SysMonitor.App.ViewModels;

public partial class ImageToolsViewModel : ObservableObject
{
    private readonly IImageTools _imageTools;
    private readonly DispatcherQueue _dispatcherQueue;

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    // Selected Images
    [ObservableProperty] private ObservableCollection<SelectedImageItem> _selectedImages = [];
    [ObservableProperty] private bool _hasSelectedImages;
    [ObservableProperty] private int _selectedImagesCount;
    [ObservableProperty] private string _totalSelectedSize = "";

    // Current Image Info
    [ObservableProperty] private ImageInfo? _currentImageInfo;
    [ObservableProperty] private bool _hasImageInfo;
    [ObservableProperty] private string _imageDimensions = "";
    [ObservableProperty] private string _imageFormat = "";
    [ObservableProperty] private string _imageColorDepth = "";
    [ObservableProperty] private string _imageResolution = "";

    // Compression Options
    [ObservableProperty] private int _compressionQuality = 80;

    // Conversion Options
    [ObservableProperty] private ImageFormat _selectedFormat = ImageFormat.Jpeg;
    [ObservableProperty] private int _conversionQuality = 90;

    // Resize Options
    [ObservableProperty] private int _resizeWidth = 1920;
    [ObservableProperty] private int _resizeHeight = 1080;
    [ObservableProperty] private bool _maintainAspectRatio = true;

    // Progress & Status
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _processingStatus = "";
    [ObservableProperty] private int _processingProgress;
    [ObservableProperty] private string _actionStatus = "";
    [ObservableProperty] private bool _hasActionStatus;
    [ObservableProperty] private string _actionStatusColor = "#4CAF50";

    // Last Result
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _resultOriginalSize = "";
    [ObservableProperty] private string _resultNewSize = "";
    [ObservableProperty] private string _resultSaved = "";
    [ObservableProperty] private string _resultPath = "";
    [ObservableProperty] private double _compressionRatio;
    [ObservableProperty] private int _filesProcessed;

    public ImageFormat[] ImageFormats { get; } = Enum.GetValues<ImageFormat>();

    public ImageToolsViewModel(IImageTools imageTools)
    {
        _imageTools = imageTools;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    private async Task SelectImagesAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0)
            {
                SelectedImages.Clear();
                long totalSize = 0;

                foreach (var file in files)
                {
                    var fileInfo = new FileInfo(file.Path);
                    totalSize += fileInfo.Length;

                    SelectedImages.Add(new SelectedImageItem
                    {
                        FileName = file.Name,
                        FilePath = file.Path,
                        FileSize = FormatSize(fileInfo.Length),
                        FileSizeBytes = fileInfo.Length
                    });
                }

                HasSelectedImages = true;
                SelectedImagesCount = SelectedImages.Count;
                TotalSelectedSize = FormatSize(totalSize);
                HasResult = false;

                // Get info for first image
                if (SelectedImages.Count == 1)
                {
                    await LoadImageInfoAsync(SelectedImages[0].FilePath);
                }
                else
                {
                    HasImageInfo = false;
                    CurrentImageInfo = null;
                }
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task SelectSingleImageAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
            picker.FileTypeFilter.Add(".jpg");
            picker.FileTypeFilter.Add(".jpeg");
            picker.FileTypeFilter.Add(".png");
            picker.FileTypeFilter.Add(".bmp");
            picker.FileTypeFilter.Add(".gif");

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                SelectedImages.Clear();
                var fileInfo = new FileInfo(file.Path);

                SelectedImages.Add(new SelectedImageItem
                {
                    FileName = file.Name,
                    FilePath = file.Path,
                    FileSize = FormatSize(fileInfo.Length),
                    FileSizeBytes = fileInfo.Length
                });

                HasSelectedImages = true;
                SelectedImagesCount = 1;
                TotalSelectedSize = FormatSize(fileInfo.Length);
                HasResult = false;

                await LoadImageInfoAsync(file.Path);
            }
        }
        catch { }
    }

    private async Task LoadImageInfoAsync(string path)
    {
        var info = await _imageTools.GetImageInfoAsync(path);
        _dispatcherQueue.TryEnqueue(() =>
        {
            CurrentImageInfo = info;
            HasImageInfo = info != null;
            ImageDimensions = info?.Dimensions ?? "";
            ImageFormat = info?.Format ?? "";
            ImageColorDepth = info?.ColorDepth ?? "";
            ImageResolution = info?.Resolution ?? "";
        });
    }

    [RelayCommand]
    private async Task CompressAsync()
    {
        if (!HasSelectedImages)
        {
            ShowAction("Please select images first", false);
            return;
        }

        IsProcessing = true;
        ProcessingStatus = "Compressing images...";
        HasResult = false;
        ProcessingProgress = 0;

        try
        {
            long totalOriginal = 0;
            long totalNew = 0;
            var processed = 0;
            var outputs = new List<string>();

            foreach (var image in SelectedImages)
            {
                ProcessingStatus = $"Compressing {image.FileName}...";
                var progress = new Progress<int>(p => ProcessingProgress = p);

                var result = await _imageTools.CompressImageAsync(image.FilePath, CompressionQuality, progress);

                if (result.Success)
                {
                    totalOriginal += result.OriginalSize;
                    totalNew += result.NewSize;
                    outputs.Add(result.OutputPath);
                    processed++;
                }
            }

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (processed > 0)
                {
                    HasResult = true;
                    ResultOriginalSize = FormatSize(totalOriginal);
                    ResultNewSize = FormatSize(totalNew);
                    ResultSaved = FormatSize(totalOriginal - totalNew);
                    ResultPath = outputs.FirstOrDefault() ?? "";
                    CompressionRatio = totalOriginal > 0 ? Math.Round((1 - (double)totalNew / totalOriginal) * 100, 1) : 0;
                    FilesProcessed = processed;

                    ShowAction($"Compressed {processed} image(s) - Saved {ResultSaved} ({CompressionRatio}%)", true);
                }
                else
                {
                    ShowAction("Compression failed", false);
                }
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() => ShowAction($"Error: {ex.Message}", false));
        }
        finally
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsProcessing = false;
                ProcessingStatus = "";
                ProcessingProgress = 0;
            });
        }
    }

    [RelayCommand]
    private async Task ConvertAsync()
    {
        if (!HasSelectedImages)
        {
            ShowAction("Please select images first", false);
            return;
        }

        IsProcessing = true;
        ProcessingStatus = "Converting images...";
        HasResult = false;

        try
        {
            var paths = SelectedImages.Select(i => i.FilePath);
            var progress = new Progress<BatchImageProgress>(p =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    ProcessingStatus = p.Status;
                    ProcessingProgress = (int)p.PercentComplete;
                });
            });

            var result = await _imageTools.BatchConvertAsync(paths, SelectedFormat, ConversionQuality, progress);

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (result.Success || result.OutputFiles.Count > 0)
                {
                    HasResult = true;
                    ResultOriginalSize = FormatSize(result.OriginalSize);
                    ResultNewSize = FormatSize(result.NewSize);
                    ResultSaved = result.CompressionRatio > 0 ? FormatSize(result.OriginalSize - result.NewSize) : "N/A";
                    ResultPath = result.OutputPath;
                    CompressionRatio = result.CompressionRatio;
                    FilesProcessed = result.OutputFiles.Count;

                    ShowAction($"Converted {result.OutputFiles.Count} image(s) to {SelectedFormat}", true);
                }
                else
                {
                    ShowAction($"Conversion failed: {result.ErrorMessage}", false);
                }
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() => ShowAction($"Error: {ex.Message}", false));
        }
        finally
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsProcessing = false;
                ProcessingStatus = "";
                ProcessingProgress = 0;
            });
        }
    }

    [RelayCommand]
    private async Task ResizeAsync()
    {
        if (!HasSelectedImages)
        {
            ShowAction("Please select images first", false);
            return;
        }

        if (ResizeWidth <= 0 || ResizeHeight <= 0)
        {
            ShowAction("Please enter valid dimensions", false);
            return;
        }

        IsProcessing = true;
        ProcessingStatus = "Resizing images...";
        HasResult = false;

        try
        {
            var paths = SelectedImages.Select(i => i.FilePath);
            var progress = new Progress<BatchImageProgress>(p =>
            {
                _dispatcherQueue.TryEnqueue(() =>
                {
                    ProcessingStatus = p.Status;
                    ProcessingProgress = (int)p.PercentComplete;
                });
            });

            var result = await _imageTools.BatchResizeAsync(paths, ResizeWidth, ResizeHeight, MaintainAspectRatio, progress);

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (result.Success || result.OutputFiles.Count > 0)
                {
                    HasResult = true;
                    ResultOriginalSize = FormatSize(result.OriginalSize);
                    ResultNewSize = FormatSize(result.NewSize);
                    ResultSaved = result.CompressionRatio > 0 ? FormatSize(result.OriginalSize - result.NewSize) : "N/A";
                    ResultPath = result.OutputPath;
                    CompressionRatio = result.CompressionRatio;
                    FilesProcessed = result.OutputFiles.Count;

                    ShowAction($"Resized {result.OutputFiles.Count} image(s) to {ResizeWidth}x{ResizeHeight}", true);
                }
                else
                {
                    ShowAction($"Resize failed: {result.ErrorMessage}", false);
                }
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() => ShowAction($"Error: {ex.Message}", false));
        }
        finally
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsProcessing = false;
                ProcessingStatus = "";
                ProcessingProgress = 0;
            });
        }
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (string.IsNullOrEmpty(ResultPath)) return;

        try
        {
            var path = File.Exists(ResultPath) ? Path.GetDirectoryName(ResultPath) : ResultPath;
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
        }
        catch { }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        SelectedImages.Clear();
        HasSelectedImages = false;
        SelectedImagesCount = 0;
        TotalSelectedSize = "";
        HasResult = false;
        HasImageInfo = false;
        CurrentImageInfo = null;
        ImageDimensions = "";
        ImageFormat = "";
        ImageColorDepth = "";
        ImageResolution = "";
    }

    [RelayCommand]
    private void RemoveImage(SelectedImageItem? item)
    {
        if (item == null) return;

        SelectedImages.Remove(item);
        SelectedImagesCount = SelectedImages.Count;
        HasSelectedImages = SelectedImages.Count > 0;

        if (HasSelectedImages)
        {
            TotalSelectedSize = FormatSize(SelectedImages.Sum(i => i.FileSizeBytes));
        }
        else
        {
            TotalSelectedSize = "";
            HasImageInfo = false;
            CurrentImageInfo = null;
        }
    }

    private void ShowAction(string message, bool isSuccess)
    {
        ActionStatus = message;
        ActionStatusColor = isSuccess ? "#4CAF50" : "#F44336";
        HasActionStatus = true;
        _ = ClearActionAfterDelayAsync();
    }

    private async Task ClearActionAfterDelayAsync()
    {
        await Task.Delay(5000);
        _dispatcherQueue.TryEnqueue(() => HasActionStatus = false);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_000_000_000) return $"{bytes / 1_000_000_000.0:F2} GB";
        if (bytes >= 1_000_000) return $"{bytes / 1_000_000.0:F2} MB";
        if (bytes >= 1_000) return $"{bytes / 1_000.0:F2} KB";
        return $"{bytes} B";
    }
}

public record SelectedImageItem
{
    public string FileName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string FileSize { get; init; } = "";
    public long FileSizeBytes { get; init; }
}
