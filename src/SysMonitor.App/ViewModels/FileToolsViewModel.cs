using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Utilities;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;

namespace SysMonitor.App.ViewModels;

public partial class FileToolsViewModel : ObservableObject
{
    private readonly IFileConverter _fileConverter;
    private readonly DispatcherQueue _dispatcherQueue;

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    // Selected File
    [ObservableProperty] private string _selectedFilePath = "";
    [ObservableProperty] private string _selectedFileName = "";
    [ObservableProperty] private string _fileSize = "";
    [ObservableProperty] private bool _hasSelectedFile;

    // Compression
    [ObservableProperty] private CompressionFormat _selectedCompressionFormat = CompressionFormat.Zip;
    [ObservableProperty] private bool _isCompressing;
    [ObservableProperty] private string _compressionResult = "";

    // Progress & Status
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _processingStatus = "";
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

    public CompressionFormat[] CompressionFormats { get; } = Enum.GetValues<CompressionFormat>();

    public FileToolsViewModel(IFileConverter fileConverter)
    {
        _fileConverter = fileConverter;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    private async Task SelectFileAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                SelectedFilePath = file.Path;
                SelectedFileName = file.Name;

                var fileInfo = new FileInfo(file.Path);
                FileSize = FormatSize(fileInfo.Length);
                HasSelectedFile = true;
                HasResult = false;
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task SelectFolderAsync()
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add("*");

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                SelectedFilePath = folder.Path;
                SelectedFileName = folder.Name;

                // Calculate folder size
                var dirInfo = new DirectoryInfo(folder.Path);
                var totalSize = dirInfo.GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                FileSize = FormatSize(totalSize);
                HasSelectedFile = true;
                HasResult = false;
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task CompressAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            ShowAction("Please select a file or folder first", false);
            return;
        }

        IsProcessing = true;
        IsCompressing = true;
        ProcessingStatus = "Compressing...";
        HasResult = false;

        try
        {
            var result = await _fileConverter.CompressFileAsync(SelectedFilePath, SelectedCompressionFormat);

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (result.Success)
                {
                    HasResult = true;
                    ResultOriginalSize = FormatSize(result.OriginalSize);
                    ResultNewSize = FormatSize(result.NewSize);
                    ResultSaved = FormatSize(result.OriginalSize - result.NewSize);
                    ResultPath = result.OutputPath;
                    CompressionRatio = result.CompressionRatio;

                    ShowAction($"Compressed successfully! Saved {ResultSaved} ({result.CompressionRatio:F1}%)", true);
                }
                else
                {
                    ShowAction($"Compression failed: {result.ErrorMessage}", false);
                }
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ShowAction($"Error: {ex.Message}", false);
            });
        }
        finally
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsProcessing = false;
                IsCompressing = false;
                ProcessingStatus = "";
            });
        }
    }

    [RelayCommand]
    private async Task DecompressAsync()
    {
        if (string.IsNullOrEmpty(SelectedFilePath))
        {
            ShowAction("Please select an archive file first", false);
            return;
        }

        var extension = Path.GetExtension(SelectedFilePath).ToLowerInvariant();
        if (extension != ".zip" && extension != ".gz")
        {
            ShowAction("Only .zip and .gz files are supported for decompression", false);
            return;
        }

        IsProcessing = true;
        ProcessingStatus = "Extracting...";
        HasResult = false;

        try
        {
            var result = await _fileConverter.DecompressFileAsync(SelectedFilePath);

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (result.Success)
                {
                    HasResult = true;
                    ResultOriginalSize = FormatSize(result.OriginalSize);
                    ResultNewSize = FormatSize(result.NewSize);
                    ResultSaved = "N/A (Extraction)";
                    ResultPath = result.OutputPath;
                    CompressionRatio = 0;

                    ShowAction($"Extracted successfully to: {result.OutputPath}", true);
                }
                else
                {
                    ShowAction($"Extraction failed: {result.ErrorMessage}", false);
                }
            });
        }
        catch (Exception ex)
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                ShowAction($"Error: {ex.Message}", false);
            });
        }
        finally
        {
            _dispatcherQueue.TryEnqueue(() =>
            {
                IsProcessing = false;
                ProcessingStatus = "";
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
        SelectedFilePath = "";
        SelectedFileName = "";
        FileSize = "";
        HasSelectedFile = false;
        HasResult = false;
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
        if (bytes >= 1_000_000_000)
            return $"{bytes / 1_000_000_000.0:F2} GB";
        if (bytes >= 1_000_000)
            return $"{bytes / 1_000_000.0:F2} MB";
        if (bytes >= 1_000)
            return $"{bytes / 1_000.0:F2} KB";
        return $"{bytes} B";
    }
}
