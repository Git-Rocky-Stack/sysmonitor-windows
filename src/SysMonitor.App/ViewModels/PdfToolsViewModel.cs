using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Windows.Storage.Pickers;

namespace SysMonitor.App.ViewModels;

public partial class PdfToolsViewModel : ObservableObject
{
    private readonly IPdfTools _pdfTools;
    private readonly DispatcherQueue _dispatcherQueue;

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    public ObservableCollection<PdfFileDisplay> SelectedFiles { get; } = [];

    // Selected PDF Info
    [ObservableProperty] private string _selectedPdfPath = "";
    [ObservableProperty] private string _selectedPdfName = "";
    [ObservableProperty] private bool _hasPdfSelected;
    [ObservableProperty] private int _pageCount;
    [ObservableProperty] private string _fileSize = "";
    [ObservableProperty] private string _pdfVersion = "";
    [ObservableProperty] private string _author = "";
    [ObservableProperty] private string _title = "";

    // Merge Settings
    [ObservableProperty] private bool _isMergeMode = true;

    // Split Settings
    [ObservableProperty] private bool _isSplitMode;
    [ObservableProperty] private bool _splitAllPages = true;
    [ObservableProperty] private bool _splitByRange;
    [ObservableProperty] private int _splitStartPage = 1;
    [ObservableProperty] private int _splitEndPage = 1;
    [ObservableProperty] private int _pagesPerSplit = 1;

    // Extract Settings
    [ObservableProperty] private bool _isExtractMode;
    [ObservableProperty] private int _extractStartPage = 1;
    [ObservableProperty] private int _extractEndPage = 1;

    // Progress & Status
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _processingStatus = "";
    [ObservableProperty] private string _actionStatus = "";
    [ObservableProperty] private bool _hasActionStatus;
    [ObservableProperty] private string _actionStatusColor = "#4CAF50";

    // Last Result
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private string _resultPath = "";
    [ObservableProperty] private string _resultMessage = "";

    public PdfToolsViewModel(IPdfTools pdfTools)
    {
        _pdfTools = pdfTools;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    private async Task SelectPdfAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".pdf");

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await LoadPdfInfoAsync(file.Path);
            }
        }
        catch { }
    }

    [RelayCommand]
    private async Task SelectMultiplePdfsAsync()
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".pdf");

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var files = await picker.PickMultipleFilesAsync();
            if (files != null && files.Count > 0)
            {
                foreach (var file in files)
                {
                    if (!SelectedFiles.Any(f => f.FullPath == file.Path))
                    {
                        var info = await _pdfTools.GetPdfInfoAsync(file.Path);
                        SelectedFiles.Add(new PdfFileDisplay(file.Path, info));
                    }
                }
                IsMergeMode = true;
            }
        }
        catch { }
    }

    private async Task LoadPdfInfoAsync(string path)
    {
        if (!_pdfTools.IsValidPdf(path))
        {
            ShowAction("Invalid PDF file", false);
            return;
        }

        var info = await _pdfTools.GetPdfInfoAsync(path);

        _dispatcherQueue.TryEnqueue(() =>
        {
            SelectedPdfPath = path;
            SelectedPdfName = Path.GetFileName(path);
            HasPdfSelected = true;
            HasResult = false;

            if (info != null)
            {
                PageCount = info.PageCount;
                FileSize = FormatSize(info.FileSizeBytes);
                PdfVersion = info.PdfVersion;
                Author = info.Author ?? "Unknown";
                Title = info.Title ?? SelectedPdfName;
                SplitEndPage = info.PageCount;
                ExtractEndPage = info.PageCount;
            }
        });
    }

    [RelayCommand]
    private void RemoveFile(PdfFileDisplay file)
    {
        SelectedFiles.Remove(file);
    }

    [RelayCommand]
    private void MoveFileUp(PdfFileDisplay file)
    {
        var index = SelectedFiles.IndexOf(file);
        if (index > 0)
        {
            SelectedFiles.Move(index, index - 1);
        }
    }

    [RelayCommand]
    private void MoveFileDown(PdfFileDisplay file)
    {
        var index = SelectedFiles.IndexOf(file);
        if (index < SelectedFiles.Count - 1)
        {
            SelectedFiles.Move(index, index + 1);
        }
    }

    [RelayCommand]
    private void ClearFiles()
    {
        SelectedFiles.Clear();
    }

    [RelayCommand]
    private async Task MergePdfsAsync()
    {
        if (SelectedFiles.Count < 2)
        {
            ShowAction("Select at least 2 PDF files to merge", false);
            return;
        }

        IsProcessing = true;
        ProcessingStatus = "Merging PDFs...";

        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("PDF Document", [".pdf"]);
            picker.SuggestedFileName = "merged";

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                IsProcessing = false;
                return;
            }

            var result = await _pdfTools.MergePdfsAsync(
                SelectedFiles.Select(f => f.FullPath),
                file.Path);

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (result.Success)
                {
                    HasResult = true;
                    ResultPath = result.OutputPath;
                    ResultMessage = $"Merged {SelectedFiles.Count} PDFs successfully";
                    ShowAction(ResultMessage, true);
                }
                else
                {
                    ShowAction($"Merge failed: {result.ErrorMessage}", false);
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
            });
        }
    }

    [RelayCommand]
    private async Task SplitPdfAsync()
    {
        if (!HasPdfSelected)
        {
            ShowAction("Select a PDF file first", false);
            return;
        }

        IsProcessing = true;
        ProcessingStatus = "Splitting PDF...";

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
            if (folder == null)
            {
                IsProcessing = false;
                return;
            }

            var options = new SplitOptions
            {
                SplitAllPages = SplitAllPages,
                StartPage = SplitStartPage,
                EndPage = SplitEndPage,
                PagesPerFile = PagesPerSplit
            };

            var result = await _pdfTools.SplitPdfAsync(SelectedPdfPath, folder.Path, options);

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (result.Success)
                {
                    HasResult = true;
                    ResultPath = result.OutputPath;
                    ResultMessage = $"Split PDF into {result.PagesProcessed} files";
                    ShowAction(ResultMessage, true);
                }
                else
                {
                    ShowAction($"Split failed: {result.ErrorMessage}", false);
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
            });
        }
    }

    [RelayCommand]
    private async Task ExtractPagesAsync()
    {
        if (!HasPdfSelected)
        {
            ShowAction("Select a PDF file first", false);
            return;
        }

        if (ExtractStartPage > ExtractEndPage || ExtractStartPage < 1 || ExtractEndPage > PageCount)
        {
            ShowAction("Invalid page range", false);
            return;
        }

        IsProcessing = true;
        ProcessingStatus = "Extracting pages...";

        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("PDF Document", [".pdf"]);
            picker.SuggestedFileName = $"{Path.GetFileNameWithoutExtension(SelectedPdfName)}_pages_{ExtractStartPage}-{ExtractEndPage}";

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSaveFileAsync();
            if (file == null)
            {
                IsProcessing = false;
                return;
            }

            var result = await _pdfTools.ExtractPagesAsync(
                SelectedPdfPath, file.Path, ExtractStartPage, ExtractEndPage);

            _dispatcherQueue.TryEnqueue(() =>
            {
                if (result.Success)
                {
                    HasResult = true;
                    ResultPath = result.OutputPath;
                    ResultMessage = $"Extracted pages {ExtractStartPage}-{ExtractEndPage}";
                    ShowAction(ResultMessage, true);
                }
                else
                {
                    ShowAction($"Extract failed: {result.ErrorMessage}", false);
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
    private void SetMode(string mode)
    {
        IsMergeMode = mode == "Merge";
        IsSplitMode = mode == "Split";
        IsExtractMode = mode == "Extract";
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

public class PdfFileDisplay
{
    public string FullPath { get; }
    public string FileName { get; }
    public int PageCount { get; }
    public string FileSize { get; }

    public PdfFileDisplay(string path, PdfInfo? info)
    {
        FullPath = path;
        FileName = Path.GetFileName(path);
        PageCount = info?.PageCount ?? 0;
        FileSize = FormatSize(info?.FileSizeBytes ?? new FileInfo(path).Length);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_000_000)
            return $"{bytes / 1_000_000.0:F2} MB";
        if (bytes >= 1_000)
            return $"{bytes / 1_000.0:F2} KB";
        return $"{bytes} B";
    }
}
