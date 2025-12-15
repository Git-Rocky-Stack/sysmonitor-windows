using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SysMonitor.App.ViewModels;
using SysMonitor.Core.Services.Utilities;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Storage.Pickers;
using Windows.UI;

namespace SysMonitor.App.Views;

public sealed partial class PdfEditorPage : Page
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetActiveWindow();

    public PdfEditorViewModel ViewModel { get; }

    // Annotation drawing state
    private bool _isDrawing;
    private Point _startPoint;
    private Shape? _previewShape;
    private TextBox? _textInputBox;

    // Freehand/Signature drawing state
    private bool _isFreehandDrawing;
    private Polyline? _currentPolyline;
    private List<PointData> _freehandPoints = [];
    private List<List<PointData>> _signatureStrokes = [];
    private List<Polyline> _signaturePolylines = [];

    // Sticky note input state
    private bool _isEditingStickyNote;
    private Border? _stickyNoteInput;

    // Annotation selection state
    private readonly Dictionary<Guid, FrameworkElement> _annotationElements = new();
    private Guid? _selectedAnnotationId;
    private Border? _selectionBorder;

    public PdfEditorPage()
    {
        ViewModel = App.GetService<PdfEditorViewModel>();
        InitializeComponent();

        // Handle keyboard events for delete
        this.KeyDown += Page_KeyDown;
    }

    // Keyboard handler for Delete key
    private void Page_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Delete && _selectedAnnotationId.HasValue)
        {
            DeleteSelectedAnnotation();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            ClearSelection();
            e.Handled = true;
        }
    }

    // File Operations
    private async void OpenPdf_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenPdfCommand.ExecuteAsync(null);
        // Clear annotations when opening new document
        ClearAnnotationVisuals();
    }

    private async void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);
    }

    // Page Navigation
    private async void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PreviousPageCommand.ExecuteAsync(null);
        ClearAnnotationVisuals();
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.NextPageCommand.ExecuteAsync(null);
        ClearAnnotationVisuals();
    }

    private async void PageThumbnail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int pageNumber)
        {
            await ViewModel.NavigateToPageCommand.ExecuteAsync(pageNumber);
            ClearAnnotationVisuals();
        }
    }

    // Page Operations
    private async void RotateLeft_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RotateCurrentPageCommand.ExecuteAsync(-90);
    }

    private async void RotateRight_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RotateCurrentPageCommand.ExecuteAsync(90);
    }

    private async void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.DeleteCurrentPageCommand.ExecuteAsync(null);
    }

    private async void MovePageUp_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.MovePageUpCommand.ExecuteAsync(null);
    }

    private async void MovePageDown_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.MovePageDownCommand.ExecuteAsync(null);
    }

    // Annotation Tools
    private void SelectTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string toolName)
        {
            ViewModel.SelectToolCommand.Execute(toolName);
        }
    }

    private void ClearAnnotations_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearAnnotationsCommand.Execute(null);
        ClearAnnotationVisuals();
    }

    private void ClearAnnotationVisuals()
    {
        AnnotationCanvas.Children.Clear();
        _annotationElements.Clear();
        _selectedAnnotationId = null;
        _selectionBorder = null;
    }

    // Formatting Controls
    private void ColorPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string color)
        {
            ViewModel.AnnotationColor = color;
        }
    }

    private void FontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string sizeStr)
        {
            if (double.TryParse(sizeStr, out double size))
            {
                ViewModel.FontSize = size;
            }
        }
    }

    private void StrokeWidth_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item && item.Tag is string widthStr)
        {
            if (double.TryParse(widthStr, out double width))
            {
                ViewModel.StrokeWidth = width;
            }
        }
    }

    private void BoldToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            ViewModel.IsBold = toggleButton.IsChecked ?? false;
        }
    }

    private void ItalicToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton)
        {
            ViewModel.IsItalic = toggleButton.IsChecked ?? false;
        }
    }

    // Zoom Controls
    private void ZoomIn_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ZoomInCommand.Execute(null);
    }

    private void ZoomOut_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ZoomOutCommand.Execute(null);
    }

    private void ZoomReset_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ResetZoomCommand.Execute(null);
    }

    // New button handlers
    private async void ExportToWord_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ExportToWordCommand.ExecuteAsync(null);
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.UndoCommand.Execute(null);
        RefreshAnnotationVisuals();
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.RedoCommand.Execute(null);
        RefreshAnnotationVisuals();
    }

    private async void InsertImage_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.SuggestedStartLocation = PickerLocationId.PicturesLibrary;
        picker.FileTypeFilter.Add(".png");
        picker.FileTypeFilter.Add(".jpg");
        picker.FileTypeFilter.Add(".jpeg");
        picker.FileTypeFilter.Add(".gif");
        picker.FileTypeFilter.Add(".bmp");

        var hwnd = GetActiveWindow();
        if (hwnd != IntPtr.Zero)
        {
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        }

        var file = await picker.PickSingleFileAsync();
        if (file != null)
        {
            var buffer = await Windows.Storage.FileIO.ReadBufferAsync(file);
            var imageData = new byte[buffer.Length];
            using var dataReader = Windows.Storage.Streams.DataReader.FromBuffer(buffer);
            dataReader.ReadBytes(imageData);

            // Add image at center of visible area
            var annotationId = await ViewModel.AddImageAnnotationAsync(imageData, 100, 100, 200, 150);
            if (annotationId.HasValue)
            {
                AddImageVisual(annotationId.Value, imageData, 100, 100, 200, 150);
            }
        }
    }

    private void RefreshAnnotationVisuals()
    {
        ClearAnnotationVisuals();
        // Reload visuals from current annotations
        foreach (var annotation in ViewModel.CurrentAnnotations)
        {
            AddVisualFromAnnotation(annotation);
        }
    }

    private void AddVisualFromAnnotation(AnnotationViewModel annotation)
    {
        // Add visual elements for existing annotations
        // This is a simplified version - full implementation would recreate exact visuals
    }

    // Annotation Canvas Event Handlers
    private void AnnotationCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(AnnotationCanvas);
        _startPoint = point.Position;

        // In Select mode, try to select an annotation at click position
        if (ViewModel.SelectedTool == AnnotationTool.None)
        {
            TrySelectAnnotationAt(_startPoint);
            e.Handled = true;
            return;
        }

        // Clear any current selection when starting to draw
        ClearSelection();

        // Handle freehand and signature tools with continuous drawing
        if (ViewModel.SelectedTool == AnnotationTool.Pen || ViewModel.SelectedTool == AnnotationTool.Signature)
        {
            StartFreehandDrawing(point.Position);
            AnnotationCanvas.CapturePointer(e.Pointer);
            e.Handled = true;
            return;
        }

        // Handle text tool differently - show text input immediately
        if (ViewModel.SelectedTool == AnnotationTool.Text)
        {
            ShowTextInput(_startPoint);
            e.Handled = true;
            return;
        }

        // Handle sticky note tool
        if (ViewModel.SelectedTool == AnnotationTool.StickyNote)
        {
            ShowStickyNoteInput(_startPoint);
            e.Handled = true;
            return;
        }

        _isDrawing = true;

        // Capture pointer for tracking
        AnnotationCanvas.CapturePointer(e.Pointer);

        // Create preview shape
        _previewShape = CreatePreviewShape();
        if (_previewShape != null)
        {
            Canvas.SetLeft(_previewShape, _startPoint.X);
            Canvas.SetTop(_previewShape, _startPoint.Y);
            PreviewCanvas.Children.Add(_previewShape);
        }

        e.Handled = true;
    }

    private void StartFreehandDrawing(Point position)
    {
        _isFreehandDrawing = true;
        _freehandPoints = [new PointData(position.X, position.Y)];

        var color = ParseColor(ViewModel.AnnotationColor);
        _currentPolyline = new Polyline
        {
            Stroke = new SolidColorBrush(color),
            StrokeThickness = ViewModel.StrokeWidth,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        _currentPolyline.Points.Add(position);

        AnnotationCanvas.Children.Add(_currentPolyline);

        // For signature tool, track as a stroke
        if (ViewModel.SelectedTool == AnnotationTool.Signature)
        {
            _signaturePolylines.Add(_currentPolyline);
        }
    }

    private void ShowStickyNoteInput(Point position)
    {
        _isEditingStickyNote = true;

        var noteColor = Color.FromArgb(255, 255, 255, 136); // Yellow

        _stickyNoteInput = new Border
        {
            Width = 200,
            MinHeight = 120,
            Background = new SolidColorBrush(noteColor),
            BorderBrush = new SolidColorBrush(Colors.DarkGoldenrod),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8)
        };

        var stackPanel = new StackPanel { Spacing = 8 };

        var titleBox = new TextBox
        {
            PlaceholderText = "Title",
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0, 0, 0, 1)
        };

        var contentBox = new TextBox
        {
            PlaceholderText = "Enter note content...",
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 60,
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0)
        };

        var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };

        var saveButton = new Button { Content = "Save", Style = (Style)Resources["AccentButtonStyle"] };
        saveButton.Click += async (s, args) =>
        {
            var title = titleBox.Text;
            var content = contentBox.Text;
            if (!string.IsNullOrWhiteSpace(title) || !string.IsNullOrWhiteSpace(content))
            {
                ViewModel.AnnotationText = content;
                var x = Canvas.GetLeft(_stickyNoteInput!);
                var y = Canvas.GetTop(_stickyNoteInput!);
                await ViewModel.AddAnnotationAtPositionAsync(x, y, 200, 120);
                AddStickyNoteVisual(Guid.NewGuid(), title, content, x, y);
            }
            CloseStickyNoteInput();
        };

        var cancelButton = new Button { Content = "Cancel" };
        cancelButton.Click += (s, args) => CloseStickyNoteInput();

        buttonPanel.Children.Add(cancelButton);
        buttonPanel.Children.Add(saveButton);

        stackPanel.Children.Add(titleBox);
        stackPanel.Children.Add(contentBox);
        stackPanel.Children.Add(buttonPanel);

        _stickyNoteInput.Child = stackPanel;

        Canvas.SetLeft(_stickyNoteInput, position.X);
        Canvas.SetTop(_stickyNoteInput, position.Y);
        AnnotationCanvas.Children.Add(_stickyNoteInput);

        titleBox.Focus(FocusState.Programmatic);
    }

    private void CloseStickyNoteInput()
    {
        if (_stickyNoteInput != null)
        {
            AnnotationCanvas.Children.Remove(_stickyNoteInput);
            _stickyNoteInput = null;
        }
        _isEditingStickyNote = false;
    }

    private void AddStickyNoteVisual(Guid id, string title, string content, double x, double y)
    {
        var noteColor = Color.FromArgb(255, 255, 255, 136);

        var note = new Border
        {
            Width = 150,
            Height = 100,
            Background = new SolidColorBrush(noteColor),
            BorderBrush = new SolidColorBrush(Colors.DarkGoldenrod),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8)
        };

        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            FontSize = 11,
            Foreground = new SolidColorBrush(Colors.Black),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        stack.Children.Add(new TextBlock
        {
            Text = content,
            FontSize = 10,
            Foreground = new SolidColorBrush(Colors.Black),
            TextWrapping = TextWrapping.Wrap,
            MaxLines = 4,
            TextTrimming = TextTrimming.CharacterEllipsis
        });

        note.Child = stack;

        Canvas.SetLeft(note, x);
        Canvas.SetTop(note, y);
        AnnotationCanvas.Children.Add(note);
        _annotationElements[id] = note;
    }

    private void AddImageVisual(Guid id, byte[] imageData, double x, double y, double width, double height)
    {
        var image = new Microsoft.UI.Xaml.Controls.Image
        {
            Width = width,
            Height = height,
            Stretch = Stretch.Uniform
        };

        // Load image from bytes
        _ = LoadImageAsync(image, imageData);

        var border = new Border
        {
            Child = image,
            BorderBrush = new SolidColorBrush(Colors.Gray),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255))
        };

        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        AnnotationCanvas.Children.Add(border);
        _annotationElements[id] = border;
    }

    private async System.Threading.Tasks.Task LoadImageAsync(Microsoft.UI.Xaml.Controls.Image image, byte[] data)
    {
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        await stream.WriteAsync(data.AsBuffer());
        stream.Seek(0);
        var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
        await bitmap.SetSourceAsync(stream);
        image.Source = bitmap;
    }

    private void AnnotationCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        // Handle freehand drawing
        if (_isFreehandDrawing && _currentPolyline != null)
        {
            var point = e.GetCurrentPoint(AnnotationCanvas);
            var position = point.Position;

            _currentPolyline.Points.Add(position);
            _freehandPoints.Add(new PointData(position.X, position.Y));
            e.Handled = true;
            return;
        }

        if (!_isDrawing || _previewShape == null)
            return;

        var currentPoint = e.GetCurrentPoint(AnnotationCanvas).Position;
        UpdatePreviewShape(currentPoint);
        e.Handled = true;
    }

    private void AnnotationCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        // Handle freehand drawing completion
        if (_isFreehandDrawing)
        {
            AnnotationCanvas.ReleasePointerCapture(e.Pointer);
            FinalizeFreehandDrawing();
            e.Handled = true;
            return;
        }

        if (!_isDrawing)
            return;

        var point = e.GetCurrentPoint(AnnotationCanvas);
        var endPoint = point.Position;

        // Release pointer capture
        AnnotationCanvas.ReleasePointerCapture(e.Pointer);

        // Finalize the annotation
        if (_previewShape != null)
        {
            FinalizeAnnotation(endPoint);
            PreviewCanvas.Children.Clear();
            _previewShape = null;
        }

        _isDrawing = false;
        e.Handled = true;
    }

    private async void FinalizeFreehandDrawing()
    {
        if (_freehandPoints.Count < 2)
        {
            // Not enough points for a stroke
            if (_currentPolyline != null)
            {
                AnnotationCanvas.Children.Remove(_currentPolyline);
            }
            _isFreehandDrawing = false;
            _currentPolyline = null;
            _freehandPoints.Clear();
            return;
        }

        // Calculate bounding box
        var minX = _freehandPoints.Min(p => p.X);
        var minY = _freehandPoints.Min(p => p.Y);
        var maxX = _freehandPoints.Max(p => p.X);
        var maxY = _freehandPoints.Max(p => p.Y);

        if (ViewModel.SelectedTool == AnnotationTool.Pen)
        {
            // Save pen stroke as freehand annotation
            var annotationId = await ViewModel.AddFreehandAnnotationAsync(_freehandPoints.ToList(), minX, minY, maxX, maxY);
            if (annotationId.HasValue && _currentPolyline != null)
            {
                _annotationElements[annotationId.Value] = _currentPolyline;
            }
        }
        else if (ViewModel.SelectedTool == AnnotationTool.Signature)
        {
            // Add stroke to signature strokes
            _signatureStrokes.Add(_freehandPoints.ToList());
            // Note: Signature is finalized when user clicks Save Signature or changes tool
        }

        _isFreehandDrawing = false;
        _currentPolyline = null;
        _freehandPoints.Clear();
    }

    // Method to finalize signature (can be called from a "Finish Signature" button)
    private async void FinalizeSignature()
    {
        if (_signatureStrokes.Count == 0) return;

        // Calculate bounding box for all strokes
        var allPoints = _signatureStrokes.SelectMany(s => s).ToList();
        if (allPoints.Count == 0) return;

        var minX = allPoints.Min(p => p.X);
        var minY = allPoints.Min(p => p.Y);
        var maxX = allPoints.Max(p => p.X);
        var maxY = allPoints.Max(p => p.Y);

        // Normalize strokes relative to bounding box
        var normalizedStrokes = _signatureStrokes.Select(stroke =>
            stroke.Select(p => new PointData(p.X - minX, p.Y - minY)).ToList()
        ).ToList();

        var signerName = await ShowSignerNameDialog();
        var annotationId = await ViewModel.AddSignatureAnnotationAsync(normalizedStrokes, minX, minY, maxX - minX, maxY - minY, signerName);

        // Clear signature strokes tracking
        _signatureStrokes.Clear();
        _signaturePolylines.Clear();
    }

    private async Task<string> ShowSignerNameDialog()
    {
        var dialog = new ContentDialog
        {
            Title = "Sign Document",
            Content = new TextBox { PlaceholderText = "Enter your name", Width = 300 },
            PrimaryButtonText = "Sign",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && dialog.Content is TextBox textBox)
        {
            return textBox.Text;
        }
        return "";
    }

    private void TrySelectAnnotationAt(Point position)
    {
        ClearSelection();

        var hit = FindAnnotationAt(position);
        if (hit.HasValue)
        {
            SelectAnnotation(hit.Value.id, hit.Value.element);
        }
    }

    private (Guid id, FrameworkElement element)? FindAnnotationAt(Point position)
    {
        // Find annotation at click position (iterate in reverse to get topmost)
        foreach (var kvp in _annotationElements.Reverse())
        {
            var element = kvp.Value;
            var left = Canvas.GetLeft(element);
            var top = Canvas.GetTop(element);
            var right = left + element.ActualWidth;
            var bottom = top + element.ActualHeight;

            // For lines, use a larger hit area
            if (element is Line line)
            {
                var lineLeft = left + Math.Min(0, line.X2);
                var lineTop = top + Math.Min(0, line.Y2);
                var lineRight = left + Math.Max(0, line.X2);
                var lineBottom = top + Math.Max(0, line.Y2);

                // Expand hit area for lines
                if (position.X >= lineLeft - 10 && position.X <= lineRight + 10 &&
                    position.Y >= lineTop - 10 && position.Y <= lineBottom + 10)
                {
                    return (kvp.Key, element);
                }
            }
            else if (position.X >= left && position.X <= right &&
                     position.Y >= top && position.Y <= bottom)
            {
                return (kvp.Key, element);
            }
        }
        return null;
    }

    private void AnnotationCanvas_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        var position = e.GetPosition(AnnotationCanvas);
        var hit = FindAnnotationAt(position);

        if (hit.HasValue)
        {
            var element = hit.Value.element;
            var id = hit.Value.id;

            // Check if it's a text annotation (Border containing TextBlock)
            if (element is Border border && border.Child is TextBlock textBlock)
            {
                EditTextAnnotation(id, textBlock, border);
                e.Handled = true;
            }
        }
    }

    private void SelectAnnotation(Guid id, FrameworkElement element)
    {
        _selectedAnnotationId = id;

        // Create selection border
        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);

        double width, height;
        if (element is Line line)
        {
            width = Math.Abs(line.X2) + 10;
            height = Math.Abs(line.Y2) + 10;
            left -= 5;
            top -= 5;
        }
        else
        {
            width = element.ActualWidth + 4;
            height = element.ActualHeight + 4;
            left -= 2;
            top -= 2;
        }

        _selectionBorder = new Border
        {
            Width = width,
            Height = height,
            BorderBrush = new SolidColorBrush(Color.FromArgb(255, 0, 120, 215)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromArgb(30, 0, 120, 215)),
            IsHitTestVisible = false
        };

        Canvas.SetLeft(_selectionBorder, left);
        Canvas.SetTop(_selectionBorder, top);
        AnnotationCanvas.Children.Add(_selectionBorder);
    }

    private void ClearSelection()
    {
        if (_selectionBorder != null)
        {
            AnnotationCanvas.Children.Remove(_selectionBorder);
            _selectionBorder = null;
        }
        _selectedAnnotationId = null;
    }

    private void DeleteSelectedAnnotation()
    {
        if (!_selectedAnnotationId.HasValue)
            return;

        var id = _selectedAnnotationId.Value;

        // Remove visual element
        if (_annotationElements.TryGetValue(id, out var element))
        {
            AnnotationCanvas.Children.Remove(element);
            _annotationElements.Remove(id);
        }

        // Remove from ViewModel
        var annotation = ViewModel.CurrentAnnotations.FirstOrDefault(a => a.Id == id);
        if (annotation != null)
        {
            ViewModel.RemoveAnnotationCommand.Execute(annotation);
        }

        ClearSelection();
    }

    private Shape? CreatePreviewShape()
    {
        var color = ParseColor(ViewModel.AnnotationColor);
        var strokeBrush = new SolidColorBrush(color);

        return ViewModel.SelectedTool switch
        {
            AnnotationTool.Highlight => new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 0)),
                Stroke = null,
                Width = 0,
                Height = 0
            },
            AnnotationTool.Rectangle => new Rectangle
            {
                Stroke = strokeBrush,
                StrokeThickness = ViewModel.StrokeWidth,
                Fill = null,
                Width = 0,
                Height = 0
            },
            AnnotationTool.Ellipse => new Ellipse
            {
                Stroke = strokeBrush,
                StrokeThickness = ViewModel.StrokeWidth,
                Fill = null,
                Width = 0,
                Height = 0
            },
            AnnotationTool.Line or AnnotationTool.Arrow => new Line
            {
                Stroke = strokeBrush,
                StrokeThickness = ViewModel.StrokeWidth,
                X1 = 0,
                Y1 = 0,
                X2 = 0,
                Y2 = 0
            },
            AnnotationTool.Redaction => new Rectangle
            {
                Fill = new SolidColorBrush(Colors.Black),
                Stroke = new SolidColorBrush(Colors.Red),
                StrokeThickness = 2,
                StrokeDashArray = new DoubleCollection { 4, 2 },
                Width = 0,
                Height = 0
            },
            _ => null
        };
    }

    private void UpdatePreviewShape(Point currentPoint)
    {
        if (_previewShape == null) return;

        var width = currentPoint.X - _startPoint.X;
        var height = currentPoint.Y - _startPoint.Y;

        switch (_previewShape)
        {
            case Rectangle rect:
                var rectX = width >= 0 ? _startPoint.X : currentPoint.X;
                var rectY = height >= 0 ? _startPoint.Y : currentPoint.Y;
                Canvas.SetLeft(rect, rectX);
                Canvas.SetTop(rect, rectY);
                rect.Width = Math.Abs(width);
                rect.Height = Math.Abs(height);
                break;

            case Ellipse ellipse:
                var ellipseX = width >= 0 ? _startPoint.X : currentPoint.X;
                var ellipseY = height >= 0 ? _startPoint.Y : currentPoint.Y;
                Canvas.SetLeft(ellipse, ellipseX);
                Canvas.SetTop(ellipse, ellipseY);
                ellipse.Width = Math.Abs(width);
                ellipse.Height = Math.Abs(height);
                break;

            case Line line:
                line.X2 = width;
                line.Y2 = height;
                break;
        }
    }

    private async void FinalizeAnnotation(Point endPoint)
    {
        var x = Math.Min(_startPoint.X, endPoint.X);
        var y = Math.Min(_startPoint.Y, endPoint.Y);
        var width = Math.Abs(endPoint.X - _startPoint.X);
        var height = Math.Abs(endPoint.Y - _startPoint.Y);

        // Minimum size check
        if (width < 5 && height < 5)
        {
            width = Math.Max(width, 50);
            height = Math.Max(height, 20);
        }

        // Add annotation to the view model and get the ID
        var annotationId = await ViewModel.AddAnnotationAtPositionAsync(x, y, width, height);

        // Add visual annotation to the canvas if ID was returned
        if (annotationId.HasValue)
        {
            AddVisualAnnotation(annotationId.Value, x, y, width, height);
        }
    }

    private void AddVisualAnnotation(Guid id, double x, double y, double width, double height)
    {
        var color = ParseColor(ViewModel.AnnotationColor);
        FrameworkElement? element = null;

        switch (ViewModel.SelectedTool)
        {
            case AnnotationTool.Highlight:
                element = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 0)),
                    Width = width,
                    Height = height
                };
                break;

            case AnnotationTool.Rectangle:
                element = new Rectangle
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = ViewModel.StrokeWidth,
                    Width = width,
                    Height = height
                };
                break;

            case AnnotationTool.Ellipse:
                element = new Ellipse
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = ViewModel.StrokeWidth,
                    Width = width,
                    Height = height
                };
                break;

            case AnnotationTool.Line:
                element = new Line
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = ViewModel.StrokeWidth,
                    X1 = 0,
                    Y1 = 0,
                    X2 = width,
                    Y2 = height
                };
                break;

            case AnnotationTool.Arrow:
                var arrowLine = new Line
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = ViewModel.StrokeWidth,
                    X1 = 0,
                    Y1 = 0,
                    X2 = width,
                    Y2 = height
                };
                element = arrowLine;
                AddArrowHead(x + width, y + height, width, height, color);
                break;

            case AnnotationTool.Redaction:
                var redactionRect = new Rectangle
                {
                    Fill = new SolidColorBrush(Colors.Black),
                    Width = width,
                    Height = height
                };
                element = redactionRect;

                // Add "REDACTED" text overlay
                var textBlock = new TextBlock
                {
                    Text = "REDACTED",
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = Math.Min(width / 8, 14),
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var container = new Grid
                {
                    Width = width,
                    Height = height
                };
                container.Children.Add(redactionRect);
                container.Children.Add(textBlock);
                element = null; // Use container instead

                Canvas.SetLeft(container, x);
                Canvas.SetTop(container, y);
                AnnotationCanvas.Children.Add(container);
                _annotationElements[id] = container;
                return;
        }

        if (element != null)
        {
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);

            AnnotationCanvas.Children.Add(element);
            _annotationElements[id] = element;
        }
    }

    private void AddArrowHead(double tipX, double tipY, double dx, double dy, Color color)
    {
        var angle = Math.Atan2(dy, dx);
        var arrowLength = 12;
        var arrowAngle = Math.PI / 6;

        var x1 = tipX - arrowLength * Math.Cos(angle - arrowAngle);
        var y1 = tipY - arrowLength * Math.Sin(angle - arrowAngle);
        var x2 = tipX - arrowLength * Math.Cos(angle + arrowAngle);
        var y2 = tipY - arrowLength * Math.Sin(angle + arrowAngle);

        var path = new Polygon
        {
            Fill = new SolidColorBrush(color),
            Points = new PointCollection
            {
                new Point(tipX, tipY),
                new Point(x1, y1),
                new Point(x2, y2)
            }
        };

        AnnotationCanvas.Children.Add(path);
    }

    private void ShowTextInput(Point position, string existingText = "", Guid? editId = null)
    {
        // Remove any existing text input
        if (_textInputBox != null)
        {
            AnnotationCanvas.Children.Remove(_textInputBox);
        }

        _textInputBox = new TextBox
        {
            Width = 200,
            MinHeight = 30,
            FontSize = ViewModel.FontSize,
            FontWeight = ViewModel.IsBold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = ViewModel.IsItalic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            PlaceholderText = "Enter text...",
            AcceptsReturn = false,
            Text = existingText,
            Tag = editId // Store the ID if we're editing
        };

        _textInputBox.KeyDown += TextInputBox_KeyDown;
        _textInputBox.LostFocus += TextInputBox_LostFocus;

        Canvas.SetLeft(_textInputBox, position.X);
        Canvas.SetTop(_textInputBox, position.Y);
        AnnotationCanvas.Children.Add(_textInputBox);

        _textInputBox.Focus(FocusState.Programmatic);
        _textInputBox.SelectAll();
    }

    private void EditTextAnnotation(Guid id, TextBlock textBlock, FrameworkElement container)
    {
        var x = Canvas.GetLeft(container);
        var y = Canvas.GetTop(container);
        var text = textBlock.Text;

        // Hide the container (Border holding the TextBlock)
        container.Visibility = Visibility.Collapsed;

        // Show text input with existing text
        ShowTextInput(new Point(x, y), text, id);
    }

    private async void TextInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && _textInputBox != null)
        {
            await FinalizeTextAnnotation();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape && _textInputBox != null)
        {
            CancelTextInput();
            e.Handled = true;
        }
    }

    private async void TextInputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_textInputBox != null && !string.IsNullOrWhiteSpace(_textInputBox.Text))
        {
            await FinalizeTextAnnotation();
        }
        else if (_textInputBox != null)
        {
            CancelTextInput();
        }
    }

    private void CancelTextInput()
    {
        if (_textInputBox == null) return;

        // If editing, restore the original container
        if (_textInputBox.Tag is Guid editId && _annotationElements.TryGetValue(editId, out var element))
        {
            // The element is the Border container
            element.Visibility = Visibility.Visible;
        }

        AnnotationCanvas.Children.Remove(_textInputBox);
        _textInputBox = null;
    }

    private async Task FinalizeTextAnnotation()
    {
        if (_textInputBox == null || string.IsNullOrWhiteSpace(_textInputBox.Text))
        {
            CancelTextInput();
            return;
        }

        var x = Canvas.GetLeft(_textInputBox);
        var y = Canvas.GetTop(_textInputBox);
        var text = _textInputBox.Text;
        var editId = _textInputBox.Tag as Guid?;

        // Set the annotation text in view model
        ViewModel.AnnotationText = text;

        // If editing existing annotation
        if (editId.HasValue && _annotationElements.TryGetValue(editId.Value, out var existingElement))
        {
            // The existing element is a Border containing a TextBlock
            if (existingElement is Border existingBorder && existingBorder.Child is TextBlock existingTb)
            {
                existingTb.Text = text;
                existingBorder.Visibility = Visibility.Visible;
                AnnotationCanvas.Children.Remove(_textInputBox);
                _textInputBox = null;
                return;
            }
        }

        // Add to view model and get the ID
        var annotationId = await ViewModel.AddAnnotationAtPositionAsync(x, y, 200, 30);
        if (!annotationId.HasValue)
        {
            CancelTextInput();
            return;
        }

        // Create text block
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = ViewModel.FontSize,
            FontWeight = ViewModel.IsBold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = ViewModel.IsItalic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            Foreground = new SolidColorBrush(ParseColor(ViewModel.AnnotationColor))
        };

        // Wrap TextBlock in a Border for proper hit-testing
        // Border with transparent background ensures the entire bounding box is clickable
        var textContainer = new Border
        {
            Child = textBlock,
            Background = new SolidColorBrush(Color.FromArgb(1, 255, 255, 255)), // Nearly transparent for hit-testing
            Padding = new Thickness(4),
            MinWidth = 20,
            MinHeight = 20
        };

        Canvas.SetLeft(textContainer, x);
        Canvas.SetTop(textContainer, y);

        AnnotationCanvas.Children.Remove(_textInputBox);
        AnnotationCanvas.Children.Add(textContainer);
        _annotationElements[annotationId.Value] = textContainer;
        _textInputBox = null;
    }

    private static Color ParseColor(string colorString)
    {
        try
        {
            if (colorString.StartsWith("#") && colorString.Length == 7)
            {
                var r = Convert.ToByte(colorString.Substring(1, 2), 16);
                var g = Convert.ToByte(colorString.Substring(3, 2), 16);
                var b = Convert.ToByte(colorString.Substring(5, 2), 16);
                return Color.FromArgb(255, r, g, b);
            }
        }
        catch { }

        return Colors.Red;
    }
}
