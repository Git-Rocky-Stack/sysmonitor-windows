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

    /// <summary>Width of a page thumbnail in the strip, in pixels.</summary>
    private const double ThumbnailWidth = 150;

    // Zoom
    [ObservableProperty] private double _zoomLevel = 1.0;

    // The zoom the page image on the canvas was rendered at. Annotation coordinates are pixels of that
    // image, so the scale that turns them into page points comes from it, not from a zoom the user may
    // have changed since (the re-render is asynchronous).
    private double _canvasZoom = 1.0;
    private double CanvasCoordinateScale => PdfPageGeometry.CanvasCoordinateScale(_canvasZoom);

    /// <summary>
    /// How many canvas units one of an annotation's own units is worth right now. An annotation drawn at one
    /// zoom is redrawn at another by multiplying its numbers by this.
    /// </summary>
    public double CanvasScaleFor(PdfAnnotation annotation) =>
        PdfPageGeometry.RedrawScale(annotation.CoordinateScale, _canvasZoom);
    [ObservableProperty] private string _zoomDisplay = "100%";

    // Current page rotation for visual display
    [ObservableProperty] private int _currentPageRotation = 0;

    /// <summary>
    /// Raised whenever <see cref="CurrentAnnotations"/> has been rebuilt - after navigating, zooming, undoing
    /// or redoing. The canvas draws the annotations, so it listens for this instead of every command
    /// remembering to tell it; forgetting to was what left annotations invisible after an undo.
    /// </summary>
    public event EventHandler? AnnotationsReloaded;

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

        for (var position = 1; position <= CurrentDocument.Pages.Count; position++)
        {
            var page = CurrentDocument.Pages[position - 1];
            var thumbnail = new PageThumbnailViewModel
            {
                PageNumber = position,
                SourcePageNumber = page.PageNumber,
                Width = page.Width,
                Height = page.Height,
                Rotation = page.Rotation
            };

            // Load thumbnail image using Windows PDF renderer
            var imageBytes = page.IsBlank
                ? PdfPageRasterizer.BlankPagePng(page.Width, page.Height, ThumbnailWidth * PdfPageGeometry.PointsPerDip / page.Width)
                : await PdfPageRenderer.RenderThumbnailAsync(CurrentDocument.FilePath, page.PageNumber, ThumbnailWidth);
            thumbnail.Image = await ToBitmapAsync(imageBytes);

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

            // The rendered page already shows the rotation it has in the file, so the preview only turns
            // it by what the user has changed since.
            CurrentPageRotation = CurrentPage.PreviewRotation;

            // Load page image for viewer using Windows PDF renderer
            var renderZoom = ZoomLevel;
            var imageBytes = CurrentPage.IsBlank
                ? PdfPageRasterizer.BlankPagePng(CurrentPage.Width, CurrentPage.Height, renderZoom)
                : await PdfPageRenderer.RenderPageAsync(CurrentDocument.FilePath, CurrentPage.PageNumber, renderZoom);

            CurrentPageImage = await ToBitmapAsync(imageBytes);
            if (CurrentPageImage != null)
            {
                _canvasZoom = renderZoom;
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

    /// <summary>
    /// Decodes rendered page bytes into an image, on whichever thread is loading the page. Doing this in a
    /// value converter would mean blocking the UI thread on an async decode.
    /// </summary>
    private static async Task<BitmapImage?> ToBitmapAsync(byte[]? imageBytes)
    {
        if (imageBytes == null || imageBytes.Length == 0) return null;

        var bitmap = new BitmapImage();
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(imageBytes.AsBuffer());
        stream.Seek(0);
        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }

    private void LoadAnnotationsForCurrentPage()
    {
        CurrentAnnotations.Clear();

        if (CurrentDocument?.Annotations != null && CurrentPage != null)
        {
            foreach (var annotation in CurrentDocument.Annotations.Where(a => a.PageId == CurrentPage.Id))
            {
                CurrentAnnotations.Add(new AnnotationViewModel(annotation));
            }
        }

        AnnotationsReloaded?.Invoke(this, EventArgs.Empty);
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

            await LoadThumbnailsAsync();

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

    private async Task RefreshThumbnailAsync(int pagePosition)
    {
        if (CurrentDocument == null) return;

        var thumbnail = PageThumbnails.FirstOrDefault(t => t.PageNumber == pagePosition);
        if (thumbnail == null || thumbnail.SourcePageNumber <= 0) return;

        var imageBytes = await PdfPageRenderer.RenderThumbnailAsync(CurrentDocument.FilePath, thumbnail.SourcePageNumber, ThumbnailWidth);
        var image = await ToBitmapAsync(imageBytes);
        if (image != null)
        {
            GetDispatcher().TryEnqueue(() => thumbnail.Image = image);
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
                    CoordinateScale = CanvasCoordinateScale,
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
                    CoordinateScale = CanvasCoordinateScale,
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
                    CoordinateScale = CanvasCoordinateScale,
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
                    CoordinateScale = CanvasCoordinateScale,
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
                    CoordinateScale = CanvasCoordinateScale,
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
                ShowStatus("Redacted. Saving turns this page into an image, so what is under the box is removed from the file — its text can no longer be selected either.", true);
                break;

            case AnnotationTool.Stamp:
                var stamp = new StampAnnotation
                {
                    CoordinateScale = CanvasCoordinateScale,
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
            CoordinateScale = CanvasCoordinateScale,
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
            CoordinateScale = CanvasCoordinateScale,
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
            CoordinateScale = CanvasCoordinateScale,
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
    private async Task ExportAnnotationReportAsync()
    {
        if (CurrentDocument == null) return;

        try
        {
            var picker = new FileSavePicker();
            picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("Word Document", [".docx"]);
            picker.SuggestedFileName = Path.GetFileNameWithoutExtension(CurrentDocument.FileName) + "_annotations";

            var hwnd = GetActiveWindow();
            if (hwnd != IntPtr.Zero)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSaveFileAsync();
            if (file == null) return;

            IsLoading = true;
            LoadingStatus = "Writing annotation report...";

            var result = await _pdfEditor.ExportAnnotationReportAsync(CurrentDocument, file.Path);
            if (result.Success)
            {
                ShowStatus($"Annotation report written to: {file.Name}", true);
            }
            else
            {
                ShowStatus($"Report failed: {result.ErrorMessage}", false);
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
            LoadAnnotationsForCurrentPage();
            IsModified = true;
            ShowStatus("Annotation removed", true);
        }
    }

    [RelayCommand]
    private void ClearAnnotations()
    {
        if (CurrentDocument == null) return;

        if (CurrentPage == null) return;

        CurrentDocument.Annotations.RemoveAll(a => a.PageId == CurrentPage.Id);
        LoadAnnotationsForCurrentPage();
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

                // A save that dropped part of the document says so. Reporting only "Saved" would be the
                // same silence that let form fields disappear without anyone noticing.
                ShowStatus(
                    result.Warnings.Count == 0
                        ? $"Saved: {file.Name}"
                        : $"Saved: {file.Name} - {string.Join(" ", result.Warnings)}",
                    result.Warnings.Count == 0);
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
            var results = await _pdfEditor.SearchAnnotationsAsync(CurrentDocument, SearchText);
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

        var result = await _pdfEditor.AddWatermarkAsync(CurrentDocument, watermark, CurrentPageNumber);
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
    /// <summary>Its place in the document: what the strip shows and navigates by.</summary>
    [ObservableProperty] private int _pageNumber;

    /// <summary>The page of the source file it shows, or 0 for a page inserted blank.</summary>
    [ObservableProperty] private int _sourcePageNumber;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _height;
    [ObservableProperty] private int _rotation;
    /// <summary>The rendered page, ready to show in the strip.</summary>
    [ObservableProperty] private BitmapImage? _image;

    [ObservableProperty] private bool _isSelected;
}

public partial class AnnotationViewModel : ObservableObject
{
    /// <summary>The annotation itself, for the canvas to draw from.</summary>
    public PdfAnnotation Annotation { get; }

    public Guid Id { get; }
    public double X { get; }
    public double Y { get; }
    public double Width { get; }
    public double Height { get; }
    public string Color { get; }
    public string TypeDisplay { get; }
    public string Icon { get; }

    public AnnotationViewModel(PdfAnnotation annotation)
    {
        Annotation = annotation;
        Id = annotation.Id;
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
}
