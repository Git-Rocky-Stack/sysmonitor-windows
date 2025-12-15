using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using SysMonitor.App.Helpers;
using SysMonitor.Core.Services.Utilities;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace SysMonitor.App.ViewModels;

public partial class PdfEditorViewModel : ObservableObject
{
    private readonly IPdfEditor _pdfEditor;
    private DispatcherQueue? _dispatcherQueue;

    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    // Document State
    [ObservableProperty] private PdfEditorDocument? _currentDocument;
    [ObservableProperty] private bool _hasDocument;
    [ObservableProperty] private bool _isModified;
    [ObservableProperty] private string _documentName = "";
    [ObservableProperty] private int _totalPages;

    // Current Page
    [ObservableProperty] private int _currentPageNumber = 1;
    [ObservableProperty] private PdfPageInfo? _currentPage;
    [ObservableProperty] private BitmapImage? _currentPageImage;

    // Page Thumbnails
    public ObservableCollection<PageThumbnailViewModel> PageThumbnails { get; } = [];
    [ObservableProperty] private PageThumbnailViewModel? _selectedThumbnail;

    // Annotation Tool State
    [ObservableProperty] private AnnotationTool _selectedTool = AnnotationTool.None;
    [ObservableProperty] private bool _isSelectToolActive = true;
    [ObservableProperty] private bool _isTextToolActive;
    [ObservableProperty] private bool _isHighlightToolActive;
    [ObservableProperty] private bool _isRectangleToolActive;
    [ObservableProperty] private bool _isEllipseToolActive;
    [ObservableProperty] private bool _isLineToolActive;
    [ObservableProperty] private bool _isArrowToolActive;
    [ObservableProperty] private bool _isPenToolActive;
    [ObservableProperty] private bool _isImageToolActive;
    [ObservableProperty] private bool _isSignatureToolActive;
    [ObservableProperty] private bool _isStickyNoteToolActive;
    [ObservableProperty] private bool _isRedactionToolActive;
    [ObservableProperty] private bool _isStampToolActive;
    [ObservableProperty] private bool _isWatermarkToolActive;

    // Stamp options
    [ObservableProperty] private StampType _selectedStampType = StampType.Approved;
    [ObservableProperty] private string _customStampText = "";
    [ObservableProperty] private bool _stampShowDate = true;

    // Search
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _isSearchPanelVisible;
    public ObservableCollection<PdfSearchResult> SearchResults { get; } = [];

    // Undo/Redo Stack
    private readonly Stack<UndoAction> _undoStack = new();
    private readonly Stack<UndoAction> _redoStack = new();
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canRedo;

    // Annotation Properties
    [ObservableProperty] private string _annotationColor = "#FF0000";
    [ObservableProperty] private double _annotationOpacity = 0.3;
    [ObservableProperty] private double _strokeWidth = 2;
    [ObservableProperty] private double _fontSize = 12;
    [ObservableProperty] private string _annotationText = "";
    [ObservableProperty] private bool _isBold;
    [ObservableProperty] private bool _isItalic;

    // Annotations on current page
    public ObservableCollection<AnnotationViewModel> CurrentAnnotations { get; } = [];

    // Processing State
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _loadingStatus = "";
    [ObservableProperty] private string _statusMessage = "";
    [ObservableProperty] private bool _hasStatusMessage;
    [ObservableProperty] private string _statusColor = "#4CAF50";

    // Zoom
    [ObservableProperty] private double _zoomLevel = 1.0;
    [ObservableProperty] private string _zoomDisplay = "100%";

    // Current page rotation for visual display
    [ObservableProperty] private int _currentPageRotation = 0;

    public PdfEditorViewModel(IPdfEditor pdfEditor)
    {
        _pdfEditor = pdfEditor;
        // Defer DispatcherQueue retrieval until first use - it may not be available during DI construction
    }

    private DispatcherQueue GetDispatcher()
    {
        _dispatcherQueue ??= DispatcherQueue.GetForCurrentThread();
        return _dispatcherQueue;
    }

    [RelayCommand]
    private async Task OpenPdfAsync()
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
                await LoadPdfAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Error opening PDF: {ex.Message}", false);
        }
    }

    public async Task LoadPdfAsync(string filePath)
    {
        IsLoading = true;
        LoadingStatus = "Opening PDF...";

        try
        {
            var document = await _pdfEditor.OpenPdfAsync(filePath);
            if (document == null)
            {
                ShowStatus("Failed to open PDF file", false);
                return;
            }

            CurrentDocument = document;
            HasDocument = true;
            DocumentName = document.FileName;
            TotalPages = document.Pages.Count;
            IsModified = false;

            // Load thumbnails
            await LoadThumbnailsAsync();

            // Navigate to first page
            CurrentPageNumber = 1;
            await LoadCurrentPageAsync();

            ShowStatus($"Opened: {document.FileName} ({document.Pages.Count} pages)", true);
        }
        catch (Exception ex)
        {
            ShowStatus($"Error: {ex.Message}", false);
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }
    }

    private async Task LoadThumbnailsAsync()
    {
        if (CurrentDocument == null) return;

        GetDispatcher().TryEnqueue(() => PageThumbnails.Clear());

        foreach (var page in CurrentDocument.Pages)
        {
            var thumbnail = new PageThumbnailViewModel
            {
                PageNumber = page.PageNumber,
                Width = page.Width,
                Height = page.Height,
                Rotation = page.Rotation
            };

            // Load thumbnail image using Windows PDF renderer
            var imageBytes = await PdfPageRenderer.RenderThumbnailAsync(CurrentDocument.FilePath, page.PageNumber, 150);
            if (imageBytes != null)
            {
                thumbnail.ImageBytes = imageBytes;
            }

            GetDispatcher().TryEnqueue(() => PageThumbnails.Add(thumbnail));
        }
    }

    private async Task LoadCurrentPageAsync()
    {
        if (CurrentDocument == null || CurrentPageNumber < 1 || CurrentPageNumber > CurrentDocument.Pages.Count)
            return;

        IsLoading = true;
        LoadingStatus = $"Loading page {CurrentPageNumber}...";

        try
        {
            CurrentPage = CurrentDocument.Pages[CurrentPageNumber - 1];

            // Update visual rotation based on page's rotation value
            CurrentPageRotation = CurrentPage.Rotation;

            // Load page image for viewer using Windows PDF renderer
            var imageBytes = await PdfPageRenderer.RenderPageAsync(CurrentDocument.FilePath, CurrentPageNumber, ZoomLevel);

            // Convert bytes to BitmapImage on UI thread (avoids converter deadlock)
            if (imageBytes != null && imageBytes.Length > 0)
            {
                var bitmap = new BitmapImage();
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(imageBytes.AsBuffer());
                stream.Seek(0);
                await bitmap.SetSourceAsync(stream);
                CurrentPageImage = bitmap;
            }
            else
            {
                CurrentPageImage = null;
            }

            // Load annotations for this page
            LoadAnnotationsForCurrentPage();

            // Update selected thumbnail
            var thumbnail = PageThumbnails.FirstOrDefault(t => t.PageNumber == CurrentPageNumber);
            if (thumbnail != null)
            {
                SelectedThumbnail = thumbnail;
            }
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }
    }

    private void LoadAnnotationsForCurrentPage()
    {
        CurrentAnnotations.Clear();

        if (CurrentDocument?.Annotations == null) return;

        foreach (var annotation in CurrentDocument.Annotations.Where(a => a.PageNumber == CurrentPageNumber))
        {
            CurrentAnnotations.Add(new AnnotationViewModel(annotation));
        }
    }

    [RelayCommand]
    private async Task NavigateToPageAsync(int pageNumber)
    {
        if (CurrentDocument == null || pageNumber < 1 || pageNumber > TotalPages)
            return;

        CurrentPageNumber = pageNumber;
        await LoadCurrentPageAsync();
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (CurrentPageNumber > 1)
        {
            await NavigateToPageAsync(CurrentPageNumber - 1);
        }
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (CurrentPageNumber < TotalPages)
        {
            await NavigateToPageAsync(CurrentPageNumber + 1);
        }
    }

    [RelayCommand]
    private async Task RotateCurrentPageAsync(int degrees)
    {
        if (CurrentDocument == null || CurrentPage == null) return;

        var result = await _pdfEditor.RotatePageAsync(CurrentDocument, CurrentPageNumber, degrees);
        if (result.Success)
        {
            IsModified = true;
            await LoadCurrentPageAsync();
            await RefreshThumbnailAsync(CurrentPageNumber);
            ShowStatus($"Rotated page {CurrentPageNumber} by {degrees}°", true);
        }
        else
        {
            ShowStatus($"Failed to rotate: {result.ErrorMessage}", false);
        }
    }

    [RelayCommand]
    private async Task DeleteCurrentPageAsync()
    {
        if (CurrentDocument == null || CurrentPage == null) return;

        if (TotalPages <= 1)
        {
            ShowStatus("Cannot delete the only page", false);
            return;
        }

        var result = await _pdfEditor.DeletePageAsync(CurrentDocument, CurrentPageNumber);
        if (result.Success)
        {
            IsModified = true;
            TotalPages = CurrentDocument.Pages.Count;

            // Remove thumbnail
            var thumbnail = PageThumbnails.FirstOrDefault(t => t.PageNumber == CurrentPageNumber);
            if (thumbnail != null)
            {
                PageThumbnails.Remove(thumbnail);
            }

            // Update page numbers
            for (int i = 0; i < PageThumbnails.Count; i++)
            {
                PageThumbnails[i].PageNumber = i + 1;
            }

            // Navigate to appropriate page
            if (CurrentPageNumber > TotalPages)
            {
                CurrentPageNumber = TotalPages;
            }
            await LoadCurrentPageAsync();

            ShowStatus($"Deleted page. Document now has {TotalPages} pages", true);
        }
        else
        {
            ShowStatus($"Failed to delete: {result.ErrorMessage}", false);
        }
    }

    [RelayCommand]
    private async Task MovePageUpAsync()
    {
        if (CurrentDocument == null || CurrentPageNumber <= 1) return;

        var newOrder = Enumerable.Range(0, CurrentDocument.Pages.Count).ToArray();
        var currentIndex = CurrentPageNumber - 1;
        (newOrder[currentIndex], newOrder[currentIndex - 1]) = (newOrder[currentIndex - 1], newOrder[currentIndex]);

        var result = await _pdfEditor.ReorderPagesAsync(CurrentDocument, newOrder);
        if (result.Success)
        {
            IsModified = true;
            await LoadThumbnailsAsync();
            CurrentPageNumber--;
            await LoadCurrentPageAsync();
            ShowStatus($"Moved page up", true);
        }
    }

    [RelayCommand]
    private async Task MovePageDownAsync()
    {
        if (CurrentDocument == null || CurrentPageNumber >= TotalPages) return;

        var newOrder = Enumerable.Range(0, CurrentDocument.Pages.Count).ToArray();
        var currentIndex = CurrentPageNumber - 1;
        (newOrder[currentIndex], newOrder[currentIndex + 1]) = (newOrder[currentIndex + 1], newOrder[currentIndex]);

        var result = await _pdfEditor.ReorderPagesAsync(CurrentDocument, newOrder);
        if (result.Success)
        {
            IsModified = true;
            await LoadThumbnailsAsync();
            CurrentPageNumber++;
            await LoadCurrentPageAsync();
            ShowStatus($"Moved page down", true);
        }
    }

    private async Task RefreshThumbnailAsync(int pageNumber)
    {
        if (CurrentDocument == null) return;

        var thumbnail = PageThumbnails.FirstOrDefault(t => t.PageNumber == pageNumber);
        if (thumbnail != null)
        {
            var imageBytes = await PdfPageRenderer.RenderThumbnailAsync(CurrentDocument.FilePath, pageNumber, 150);
            if (imageBytes != null)
            {
                GetDispatcher().TryEnqueue(() => thumbnail.ImageBytes = imageBytes);
            }
        }
    }

    [RelayCommand]
    private void SelectTool(string toolName)
    {
        // Reset all tool states
        IsSelectToolActive = false;
        IsTextToolActive = false;
        IsHighlightToolActive = false;
        IsRectangleToolActive = false;
        IsEllipseToolActive = false;
        IsLineToolActive = false;
        IsArrowToolActive = false;
        IsPenToolActive = false;
        IsImageToolActive = false;
        IsSignatureToolActive = false;
        IsStickyNoteToolActive = false;
        IsRedactionToolActive = false;
        IsStampToolActive = false;
        IsWatermarkToolActive = false;

        SelectedTool = toolName switch
        {
            "Select" => AnnotationTool.None,
            "Text" => AnnotationTool.Text,
            "Highlight" => AnnotationTool.Highlight,
            "Rectangle" => AnnotationTool.Rectangle,
            "Ellipse" => AnnotationTool.Ellipse,
            "Line" => AnnotationTool.Line,
            "Arrow" => AnnotationTool.Arrow,
            "Pen" => AnnotationTool.Pen,
            "Image" => AnnotationTool.Image,
            "Signature" => AnnotationTool.Signature,
            "StickyNote" => AnnotationTool.StickyNote,
            "Redaction" => AnnotationTool.Redaction,
            "Stamp" => AnnotationTool.Stamp,
            "Watermark" => AnnotationTool.Watermark,
            _ => AnnotationTool.None
        };

        // Set the active tool state
        switch (SelectedTool)
        {
            case AnnotationTool.None:
                IsSelectToolActive = true;
                break;
            case AnnotationTool.Text:
                IsTextToolActive = true;
                break;
            case AnnotationTool.Highlight:
                IsHighlightToolActive = true;
                break;
            case AnnotationTool.Rectangle:
                IsRectangleToolActive = true;
                break;
            case AnnotationTool.Ellipse:
                IsEllipseToolActive = true;
                break;
            case AnnotationTool.Line:
                IsLineToolActive = true;
                break;
            case AnnotationTool.Arrow:
                IsArrowToolActive = true;
                break;
            case AnnotationTool.Pen:
                IsPenToolActive = true;
                break;
            case AnnotationTool.Image:
                IsImageToolActive = true;
                break;
            case AnnotationTool.Signature:
                IsSignatureToolActive = true;
                break;
            case AnnotationTool.StickyNote:
                IsStickyNoteToolActive = true;
                break;
            case AnnotationTool.Redaction:
                IsRedactionToolActive = true;
                break;
            case AnnotationTool.Stamp:
                IsStampToolActive = true;
                break;
            case AnnotationTool.Watermark:
                IsWatermarkToolActive = true;
                break;
        }
    }

    public async Task<Guid?> AddAnnotationAtPositionAsync(double x, double y, double width, double height)
    {
        if (CurrentDocument == null || SelectedTool == AnnotationTool.None) return null;

        PdfAnnotation? annotation = null;

        switch (SelectedTool)
        {
            case AnnotationTool.Text:
                var textAnnotation = new TextAnnotation
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    Text = AnnotationText,
                    FontSize = FontSize,
                    IsBold = IsBold,
                    IsItalic = IsItalic,
                    Color = AnnotationColor
                };
                await _pdfEditor.AddTextAnnotationAsync(CurrentDocument, CurrentPageNumber, textAnnotation);
                annotation = textAnnotation;
                break;

            case AnnotationTool.Highlight:
                var highlight = new HighlightAnnotation
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    Color = "#FFFF00", // Yellow highlight
                    Opacity = AnnotationOpacity
                };
                await _pdfEditor.AddHighlightAsync(CurrentDocument, CurrentPageNumber, highlight);
                annotation = highlight;
                break;

            case AnnotationTool.Rectangle:
            case AnnotationTool.Ellipse:
            case AnnotationTool.Line:
            case AnnotationTool.Arrow:
                var shape = new ShapeAnnotation
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    Color = AnnotationColor,
                    StrokeWidth = StrokeWidth,
                    Type = SelectedTool switch
                    {
                        AnnotationTool.Rectangle => ShapeType.Rectangle,
                        AnnotationTool.Ellipse => ShapeType.Ellipse,
                        AnnotationTool.Line => ShapeType.Line,
                        AnnotationTool.Arrow => ShapeType.Arrow,
                        _ => ShapeType.Rectangle
                    }
                };
                await _pdfEditor.AddShapeAsync(CurrentDocument, CurrentPageNumber, shape);
                annotation = shape;
                break;

            case AnnotationTool.StickyNote:
                var stickyNote = new StickyNoteAnnotation
                {
                    X = x,
                    Y = y,
                    Width = Math.Max(width, 150),
                    Height = Math.Max(height, 100),
                    Title = "Note",
                    Content = AnnotationText,
                    NoteColor = "#FFFF88",
                    Color = AnnotationColor
                };
                await _pdfEditor.AddStickyNoteAsync(CurrentDocument, CurrentPageNumber, stickyNote);
                annotation = stickyNote;
                break;

            case AnnotationTool.Redaction:
                var redaction = new RedactionAnnotation
                {
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height,
                    FillColor = "#000000",
                    OverlayText = "REDACTED",
                    Color = "#000000"
                };
                await _pdfEditor.AddRedactionAsync(CurrentDocument, CurrentPageNumber, redaction);
                annotation = redaction;
                break;

            case AnnotationTool.Stamp:
                var stamp = new StampAnnotation
                {
                    X = x,
                    Y = y,
                    Width = Math.Max(width, 180),
                    Height = Math.Max(height, 60),
                    StampType = SelectedStampType,
                    CustomText = CustomStampText,
                    ShowDate = StampShowDate,
                    Color = AnnotationColor
                };
                await _pdfEditor.AddStampAsync(CurrentDocument, CurrentPageNumber, stamp);
                annotation = stamp;
                break;
        }

        if (annotation != null)
        {
            // Add to undo stack
            PushUndoAction(new UndoAction
            {
                Type = UndoActionType.AddAnnotation,
                Annotation = annotation,
                PageNumber = CurrentPageNumber
            });
        }

        IsModified = true;
        LoadAnnotationsForCurrentPage();

        return annotation?.Id;
    }

    // Special methods for freehand and signature (called from code-behind with point data)
    public async Task<Guid?> AddFreehandAnnotationAsync(List<PointData> points, double minX, double minY, double maxX, double maxY)
    {
        if (CurrentDocument == null) return null;

        var freehand = new FreehandAnnotation
        {
            X = minX,
            Y = minY,
            Width = maxX - minX,
            Height = maxY - minY,
            Points = points,
            StrokeWidth = StrokeWidth,
            Color = AnnotationColor
        };

        await _pdfEditor.AddFreehandAsync(CurrentDocument, CurrentPageNumber, freehand);

        PushUndoAction(new UndoAction
        {
            Type = UndoActionType.AddAnnotation,
            Annotation = freehand,
            PageNumber = CurrentPageNumber
        });

        IsModified = true;
        LoadAnnotationsForCurrentPage();
        return freehand.Id;
    }

    public async Task<Guid?> AddSignatureAnnotationAsync(List<List<PointData>> strokes, double x, double y, double width, double height, string signerName)
    {
        if (CurrentDocument == null) return null;

        var signature = new SignatureAnnotation
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            Strokes = strokes,
            StrokeWidth = StrokeWidth,
            SignerName = signerName,
            Color = AnnotationColor
        };

        await _pdfEditor.AddSignatureAsync(CurrentDocument, CurrentPageNumber, signature);

        PushUndoAction(new UndoAction
        {
            Type = UndoActionType.AddAnnotation,
            Annotation = signature,
            PageNumber = CurrentPageNumber
        });

        IsModified = true;
        LoadAnnotationsForCurrentPage();
        return signature.Id;
    }

    public async Task<Guid?> AddImageAnnotationAsync(byte[] imageData, double x, double y, double width, double height)
    {
        if (CurrentDocument == null) return null;

        var imageAnnotation = new ImageAnnotation
        {
            X = x,
            Y = y,
            Width = width,
            Height = height,
            ImageData = imageData,
            Color = AnnotationColor
        };

        await _pdfEditor.AddImageAsync(CurrentDocument, CurrentPageNumber, imageAnnotation);

        PushUndoAction(new UndoAction
        {
            Type = UndoActionType.AddAnnotation,
            Annotation = imageAnnotation,
            PageNumber = CurrentPageNumber
        });

        IsModified = true;
        LoadAnnotationsForCurrentPage();
        return imageAnnotation.Id;
    }

    private void PushUndoAction(UndoAction action)
    {
        _undoStack.Push(action);
        _redoStack.Clear();
        CanUndo = _undoStack.Count > 0;
        CanRedo = false;
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count == 0 || CurrentDocument == null) return;

        var action = _undoStack.Pop();
        _redoStack.Push(action);

        switch (action.Type)
        {
            case UndoActionType.AddAnnotation:
                if (action.Annotation != null)
                {
                    CurrentDocument.Annotations.Remove(action.Annotation);
                }
                break;
            case UndoActionType.RemoveAnnotation:
                if (action.Annotation != null)
                {
                    CurrentDocument.Annotations.Add(action.Annotation);
                }
                break;
        }

        LoadAnnotationsForCurrentPage();
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
        IsModified = true;
        ShowStatus("Undo successful", true);
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0 || CurrentDocument == null) return;

        var action = _redoStack.Pop();
        _undoStack.Push(action);

        switch (action.Type)
        {
            case UndoActionType.AddAnnotation:
                if (action.Annotation != null)
                {
                    CurrentDocument.Annotations.Add(action.Annotation);
                }
                break;
            case UndoActionType.RemoveAnnotation:
                if (action.Annotation != null)
                {
                    CurrentDocument.Annotations.Remove(action.Annotation);
                }
                break;
        }

        LoadAnnotationsForCurrentPage();
        CanUndo = _undoStack.Count > 0;
        CanRedo = _redoStack.Count > 0;
        IsModified = true;
        ShowStatus("Redo successful", true);
    }

    [RelayCommand]
    private async Task ExportToWordAsync()
    {
        if (CurrentDocument == null) return;

        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Word Document", [".docx"]);
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(CurrentDocument.FileName) + "_exported";

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            IsLoading = true;
            LoadingStatus = "Exporting to Word...";

            var result = await _pdfEditor.ExportToWordAsync(CurrentDocument, file.Path);
            if (result.Success)
            {
                ShowStatus($"Exported to: {file.Name}", true);
            }
            else
            {
                ShowStatus($"Export failed: {result.ErrorMessage}", false);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Export error: {ex.Message}", false);
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }
    }

    [RelayCommand]
    private void RemoveAnnotation(AnnotationViewModel annotation)
    {
        if (CurrentDocument == null) return;

        var toRemove = CurrentDocument.Annotations.FirstOrDefault(a => a.Id == annotation.Id);
        if (toRemove != null)
        {
            CurrentDocument.Annotations.Remove(toRemove);
            CurrentAnnotations.Remove(annotation);
            IsModified = true;
            ShowStatus("Annotation removed", true);
        }
    }

    [RelayCommand]
    private void ClearAnnotations()
    {
        if (CurrentDocument == null) return;

        CurrentDocument.Annotations.RemoveAll(a => a.PageNumber == CurrentPageNumber);
        CurrentAnnotations.Clear();
        IsModified = true;
        ShowStatus("All annotations on this page cleared", true);
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (CurrentDocument == null) return;

        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("PDF Document", [".pdf"]);
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(CurrentDocument.FileName) + "_edited";

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            IsLoading = true;
            LoadingStatus = "Saving PDF...";

            var result = await _pdfEditor.SavePdfAsync(CurrentDocument, file.Path);
            if (result.Success)
            {
                IsModified = false;
                ShowStatus($"Saved: {file.Name}", true);
            }
            else
            {
                ShowStatus($"Save failed: {result.ErrorMessage}", false);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Save error: {ex.Message}", false);
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }
    }

    [RelayCommand]
    private void ZoomIn()
    {
        if (ZoomLevel < 3.0)
        {
            ZoomLevel += 0.25;
            ZoomDisplay = $"{(int)(ZoomLevel * 100)}%";
            _ = LoadCurrentPageAsync();
        }
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (ZoomLevel > 0.25)
        {
            ZoomLevel -= 0.25;
            ZoomDisplay = $"{(int)(ZoomLevel * 100)}%";
            _ = LoadCurrentPageAsync();
        }
    }

    [RelayCommand]
    private void ResetZoom()
    {
        ZoomLevel = 1.0;
        ZoomDisplay = "100%";
        _ = LoadCurrentPageAsync();
    }

    // ==================== NEW FEATURES ====================

    [RelayCommand]
    private async Task InsertBlankPageAsync()
    {
        if (CurrentDocument == null) return;

        var result = await _pdfEditor.InsertBlankPageAsync(CurrentDocument, CurrentPageNumber);
        if (result.Success)
        {
            IsModified = true;
            TotalPages = CurrentDocument.Pages.Count;
            await LoadThumbnailsAsync();
            ShowStatus($"Inserted blank page after page {CurrentPageNumber}", true);
        }
        else
        {
            ShowStatus($"Failed to insert page: {result.ErrorMessage}", false);
        }
    }

    [RelayCommand]
    private async Task DuplicateCurrentPageAsync()
    {
        if (CurrentDocument == null) return;

        var result = await _pdfEditor.DuplicatePageAsync(CurrentDocument, CurrentPageNumber);
        if (result.Success)
        {
            IsModified = true;
            TotalPages = CurrentDocument.Pages.Count;
            await LoadThumbnailsAsync();
            ShowStatus($"Duplicated page {CurrentPageNumber}", true);
        }
        else
        {
            ShowStatus($"Failed to duplicate page: {result.ErrorMessage}", false);
        }
    }

    [RelayCommand]
    private void ToggleSearchPanel()
    {
        IsSearchPanelVisible = !IsSearchPanelVisible;
        if (!IsSearchPanelVisible)
        {
            SearchResults.Clear();
            SearchText = "";
        }
    }

    [RelayCommand]
    private async Task SearchInPdfAsync()
    {
        if (CurrentDocument == null || string.IsNullOrWhiteSpace(SearchText)) return;

        IsLoading = true;
        LoadingStatus = "Searching...";

        try
        {
            var results = await _pdfEditor.SearchTextAsync(CurrentDocument, SearchText);
            SearchResults.Clear();
            foreach (var result in results)
            {
                SearchResults.Add(result);
            }

            if (SearchResults.Count > 0)
            {
                ShowStatus($"Found {SearchResults.Count} result(s)", true);
            }
            else
            {
                ShowStatus("No results found", false);
            }
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }
    }

    [RelayCommand]
    private async Task GoToSearchResultAsync(PdfSearchResult result)
    {
        if (result == null) return;
        await NavigateToPageAsync(result.PageNumber);
    }

    public async Task AddWatermarkAsync(string text, bool applyToAllPages)
    {
        if (CurrentDocument == null) return;

        var watermark = new WatermarkAnnotation
        {
            Text = text,
            Type = WatermarkType.Text,
            ApplyToAllPages = applyToAllPages,
            Opacity = 0.15,
            Rotation = -45,
            FontSize = 72,
            Position = WatermarkPosition.Center,
            Color = "#888888"
        };

        var result = await _pdfEditor.AddWatermarkAsync(CurrentDocument, watermark);
        if (result.Success)
        {
            IsModified = true;
            LoadAnnotationsForCurrentPage();
            ShowStatus(applyToAllPages ? "Watermark added to all pages" : "Watermark added to current page", true);
        }
        else
        {
            ShowStatus($"Failed to add watermark: {result.ErrorMessage}", false);
        }
    }

    [RelayCommand]
    private async Task CompressPdfAsync()
    {
        if (CurrentDocument == null) return;

        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("PDF Document", [".pdf"]);
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(CurrentDocument.FileName) + "_compressed";

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            IsLoading = true;
            LoadingStatus = "Compressing PDF...";

            var result = await _pdfEditor.CompressPdfAsync(CurrentDocument.FilePath, file.Path);
            if (result.Success)
            {
                // ErrorMessage contains the size info for successful compression
                ShowStatus(result.ErrorMessage, true);
            }
            else
            {
                ShowStatus($"Compression failed: {result.ErrorMessage}", false);
            }
        }
        catch (Exception ex)
        {
            ShowStatus($"Compression error: {ex.Message}", false);
        }
        finally
        {
            IsLoading = false;
            LoadingStatus = "";
        }
    }

    [RelayCommand]
    private void PrintPdf()
    {
        if (CurrentDocument == null) return;

        try
        {
            // Open the PDF with the default system PDF viewer for printing
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = CurrentDocument.FilePath,
                UseShellExecute = true,
                Verb = "print"
            };
            System.Diagnostics.Process.Start(psi);
            ShowStatus("Sent to printer", true);
        }
        catch (Exception ex)
        {
            ShowStatus($"Print error: {ex.Message}", false);
        }
    }

    private void ShowStatus(string message, bool isSuccess)
    {
        StatusMessage = message;
        StatusColor = isSuccess ? "#4CAF50" : "#F44336";
        HasStatusMessage = true;
        _ = ClearStatusAfterDelayAsync();
    }

    private async Task ClearStatusAfterDelayAsync()
    {
        await Task.Delay(4000);
        GetDispatcher().TryEnqueue(() => HasStatusMessage = false);
    }
}

public enum AnnotationTool
{
    None,
    Text,
    Highlight,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Pen,
    Image,
    Signature,
    StickyNote,
    Redaction,
    Stamp,
    Watermark
}

public partial class PageThumbnailViewModel : ObservableObject
{
    [ObservableProperty] private int _pageNumber;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _height;
    [ObservableProperty] private int _rotation;
    [ObservableProperty] private byte[]? _imageBytes;
    [ObservableProperty] private bool _isSelected;
}

public partial class AnnotationViewModel : ObservableObject
{
    public Guid Id { get; }
    public int PageNumber { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public string Color { get; }
    public string TypeDisplay { get; }
    public string Icon { get; }

    public AnnotationViewModel(PdfAnnotation annotation)
    {
        Id = annotation.Id;
        PageNumber = annotation.PageNumber;
        X = annotation.X;
        Y = annotation.Y;
        Width = annotation.Width;
        Height = annotation.Height;
        Color = annotation.Color;

        (TypeDisplay, Icon) = annotation switch
        {
            TextAnnotation => ("Text", "\uE8D2"),
            HighlightAnnotation => ("Highlight", "\uE7E6"),
            FreehandAnnotation => ("Drawing", "\uED63"),
            ImageAnnotation => ("Image", "\uEB9F"),
            StickyNoteAnnotation => ("Note", "\uE70B"),
            RedactionAnnotation => ("Redaction", "\uE738"),
            SignatureAnnotation => ("Signature", "\uE8FA"),
            StampAnnotation => ("Stamp", "\uE7C3"),
            WatermarkAnnotation => ("Watermark", "\uE8A5"),
            LinkAnnotation => ("Link", "\uE71B"),
            ShapeAnnotation shape => shape.Type switch
            {
                ShapeType.Rectangle => ("Rectangle", "\uE739"),
                ShapeType.Ellipse => ("Ellipse", "\uEA3A"),
                ShapeType.Line => ("Line", "\uE762"),
                ShapeType.Arrow => ("Arrow", "\uE759"),
                _ => ("Shape", "\uE739")
            },
            _ => ("Unknown", "\uE712")
        };
    }
}

public enum UndoActionType
{
    AddAnnotation,
    RemoveAnnotation,
    ModifyAnnotation
}

public class UndoAction
{
    public UndoActionType Type { get; set; }
    public PdfAnnotation? Annotation { get; set; }
    public PdfAnnotation? PreviousState { get; set; }
    public int PageNumber { get; set; }
}
