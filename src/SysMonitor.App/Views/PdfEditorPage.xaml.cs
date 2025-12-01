using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SysMonitor.App.ViewModels;
using Windows.Foundation;
using Windows.UI;

namespace SysMonitor.App.Views;

public sealed partial class PdfEditorPage : Page
{
    public PdfEditorViewModel ViewModel { get; }

    // Annotation drawing state
    private bool _isDrawing;
    private Point _startPoint;
    private Shape? _previewShape;
    private TextBox? _textInputBox;

    public PdfEditorPage()
    {
        ViewModel = App.GetService<PdfEditorViewModel>();
        InitializeComponent();
    }

    // File Operations
    private async void OpenPdf_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.OpenPdfCommand.ExecuteAsync(null);
    }

    private async void SavePdf_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.SaveCommand.ExecuteAsync(null);
    }

    // Page Navigation
    private async void PreviousPage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.PreviousPageCommand.ExecuteAsync(null);
    }

    private async void NextPage_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.NextPageCommand.ExecuteAsync(null);
    }

    private async void PageThumbnail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is int pageNumber)
        {
            await ViewModel.NavigateToPageCommand.ExecuteAsync(pageNumber);
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
            UpdateCursor();
        }
    }

    private void ClearAnnotations_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ClearAnnotationsCommand.Execute(null);
        AnnotationCanvas.Children.Clear();
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

    // Annotation Canvas Event Handlers
    private void AnnotationCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedTool == AnnotationTool.None)
            return;

        var point = e.GetCurrentPoint(AnnotationCanvas);
        _startPoint = point.Position;
        _isDrawing = true;

        // Capture pointer for tracking
        AnnotationCanvas.CapturePointer(e.Pointer);

        // Handle text tool differently - show text input immediately
        if (ViewModel.SelectedTool == AnnotationTool.Text)
        {
            ShowTextInput(_startPoint);
            _isDrawing = false;
            return;
        }

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

    private void AnnotationCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing || _previewShape == null)
            return;

        var point = e.GetCurrentPoint(AnnotationCanvas);
        var currentPoint = point.Position;

        UpdatePreviewShape(currentPoint);
        e.Handled = true;
    }

    private void AnnotationCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
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

    private void UpdateCursor()
    {
        // In WinUI 3, cursor changes are handled via ProtectedCursor on the element
        // For now, we'll use the PointerEntered event approach if needed
        // The visual feedback from tool selection buttons is sufficient
    }

    private Shape? CreatePreviewShape()
    {
        var color = ParseColor(ViewModel.AnnotationColor);
        var strokeBrush = new SolidColorBrush(color);

        return ViewModel.SelectedTool switch
        {
            AnnotationTool.Highlight => new Rectangle
            {
                Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 0)), // Semi-transparent yellow
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
                // Handle negative dimensions
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

        // Add annotation to the view model
        await ViewModel.AddAnnotationAtPositionAsync(x, y, width, height);

        // Add visual annotation to the canvas
        AddVisualAnnotation(x, y, width, height);
    }

    private void AddVisualAnnotation(double x, double y, double width, double height)
    {
        var color = ParseColor(ViewModel.AnnotationColor);
        Shape? shape = null;

        switch (ViewModel.SelectedTool)
        {
            case AnnotationTool.Highlight:
                shape = new Rectangle
                {
                    Fill = new SolidColorBrush(Color.FromArgb(80, 255, 255, 0)),
                    Width = width,
                    Height = height
                };
                break;

            case AnnotationTool.Rectangle:
                shape = new Rectangle
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = ViewModel.StrokeWidth,
                    Width = width,
                    Height = height
                };
                break;

            case AnnotationTool.Ellipse:
                shape = new Ellipse
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = ViewModel.StrokeWidth,
                    Width = width,
                    Height = height
                };
                break;

            case AnnotationTool.Line:
                shape = new Line
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
                // For arrow, create a path or use line with marker
                shape = new Line
                {
                    Stroke = new SolidColorBrush(color),
                    StrokeThickness = ViewModel.StrokeWidth,
                    X1 = 0,
                    Y1 = 0,
                    X2 = width,
                    Y2 = height
                };
                // Add arrowhead as separate shapes
                AddArrowHead(x + width, y + height, width, height, color);
                break;
        }

        if (shape != null)
        {
            Canvas.SetLeft(shape, x);
            Canvas.SetTop(shape, y);
            AnnotationCanvas.Children.Add(shape);
        }
    }

    private void AddArrowHead(double tipX, double tipY, double dx, double dy, Color color)
    {
        var angle = Math.Atan2(dy, dx);
        var arrowLength = 12;
        var arrowAngle = Math.PI / 6; // 30 degrees

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

    private void ShowTextInput(Point position)
    {
        // Remove any existing text input
        if (_textInputBox != null)
        {
            AnnotationCanvas.Children.Remove(_textInputBox);
        }

        _textInputBox = new TextBox
        {
            Width = 200,
            Height = 30,
            FontSize = ViewModel.FontSize,
            FontWeight = ViewModel.IsBold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = ViewModel.IsItalic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            PlaceholderText = "Enter text...",
            AcceptsReturn = false
        };

        _textInputBox.KeyDown += TextInputBox_KeyDown;
        _textInputBox.LostFocus += TextInputBox_LostFocus;

        Canvas.SetLeft(_textInputBox, position.X);
        Canvas.SetTop(_textInputBox, position.Y);
        AnnotationCanvas.Children.Add(_textInputBox);

        _textInputBox.Focus(FocusState.Programmatic);
    }

    private async void TextInputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter && _textInputBox != null)
        {
            await FinalizeTextAnnotation();
        }
        else if (e.Key == Windows.System.VirtualKey.Escape && _textInputBox != null)
        {
            AnnotationCanvas.Children.Remove(_textInputBox);
            _textInputBox = null;
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
            AnnotationCanvas.Children.Remove(_textInputBox);
            _textInputBox = null;
        }
    }

    private async Task FinalizeTextAnnotation()
    {
        if (_textInputBox == null || string.IsNullOrWhiteSpace(_textInputBox.Text))
            return;

        var x = Canvas.GetLeft(_textInputBox);
        var y = Canvas.GetTop(_textInputBox);
        var text = _textInputBox.Text;

        // Set the annotation text in view model
        ViewModel.AnnotationText = text;

        // Add to view model
        await ViewModel.AddAnnotationAtPositionAsync(x, y, 200, 30);

        // Replace text box with text block
        var textBlock = new TextBlock
        {
            Text = text,
            FontSize = ViewModel.FontSize,
            FontWeight = ViewModel.IsBold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            FontStyle = ViewModel.IsItalic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal,
            Foreground = new SolidColorBrush(ParseColor(ViewModel.AnnotationColor))
        };

        Canvas.SetLeft(textBlock, x);
        Canvas.SetTop(textBlock, y);

        AnnotationCanvas.Children.Remove(_textInputBox);
        AnnotationCanvas.Children.Add(textBlock);
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
