using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SysMonitor.App.ViewModels;
using Windows.Foundation;

namespace SysMonitor.App.Views;

public sealed partial class PdfEditorPage : Page
{
    public PdfEditorViewModel ViewModel { get; }

    private bool _isDrawing;
    private Point _startPoint;
    private Shape? _currentShape;

    public PdfEditorPage()
    {
        ViewModel = App.GetService<PdfEditorViewModel>();
        InitializeComponent();
        DataContext = ViewModel;
    }

    private void ColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string color)
        {
            ViewModel.AnnotationColor = color;
        }
    }

    private void AnnotationCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel.SelectedTool == AnnotationTool.None) return;

        _isDrawing = true;
        _startPoint = e.GetCurrentPoint(AnnotationCanvas).Position;
        AnnotationCanvas.CapturePointer(e.Pointer);

        // Create preview shape based on selected tool
        _currentShape = CreatePreviewShape();
        if (_currentShape != null)
        {
            Canvas.SetLeft(_currentShape, _startPoint.X);
            Canvas.SetTop(_currentShape, _startPoint.Y);
            AnnotationCanvas.Children.Add(_currentShape);
        }

        e.Handled = true;
    }

    private void AnnotationCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing || _currentShape == null) return;

        var currentPoint = e.GetCurrentPoint(AnnotationCanvas).Position;
        UpdatePreviewShape(currentPoint);
        e.Handled = true;
    }

    private async void AnnotationCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing) return;

        _isDrawing = false;
        AnnotationCanvas.ReleasePointerCapture(e.Pointer);

        var endPoint = e.GetCurrentPoint(AnnotationCanvas).Position;

        // Remove preview shape
        if (_currentShape != null)
        {
            AnnotationCanvas.Children.Remove(_currentShape);
            _currentShape = null;
        }

        // Calculate bounds
        var x = Math.Min(_startPoint.X, endPoint.X);
        var y = Math.Min(_startPoint.Y, endPoint.Y);
        var width = Math.Abs(endPoint.X - _startPoint.X);
        var height = Math.Abs(endPoint.Y - _startPoint.Y);

        // For lines and arrows, store actual start/end points differently
        if (ViewModel.SelectedTool == AnnotationTool.Line || ViewModel.SelectedTool == AnnotationTool.Arrow)
        {
            x = _startPoint.X;
            y = _startPoint.Y;
            width = endPoint.X - _startPoint.X;
            height = endPoint.Y - _startPoint.Y;
        }

        // Minimum size check
        if (Math.Abs(width) < 5 && Math.Abs(height) < 5)
        {
            // For text, allow small clicks
            if (ViewModel.SelectedTool == AnnotationTool.Text)
            {
                width = 200;
                height = 30;
            }
            else
            {
                e.Handled = true;
                return;
            }
        }

        // Add annotation to document
        await ViewModel.AddAnnotationAtPositionAsync(x, y, width, height);

        // Refresh display to show annotation
        RefreshAnnotationDisplay();

        e.Handled = true;
    }

    private Shape? CreatePreviewShape()
    {
        var color = ParseColor(ViewModel.AnnotationColor);
        var strokeBrush = new SolidColorBrush(color);

        return ViewModel.SelectedTool switch
        {
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
            AnnotationTool.Highlight => new Rectangle
            {
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    (byte)(ViewModel.AnnotationOpacity * 255), 255, 255, 0)),
                Stroke = null,
                Width = 0,
                Height = 0
            },
            AnnotationTool.Text => new Rectangle
            {
                Stroke = new SolidColorBrush(Colors.DodgerBlue),
                StrokeDashArray = new DoubleCollection { 4, 2 },
                StrokeThickness = 1,
                Fill = null,
                Width = 0,
                Height = 0
            },
            _ => null
        };
    }

    private void UpdatePreviewShape(Point currentPoint)
    {
        if (_currentShape == null) return;

        var width = currentPoint.X - _startPoint.X;
        var height = currentPoint.Y - _startPoint.Y;

        if (_currentShape is Line line)
        {
            line.X2 = width;
            line.Y2 = height;
        }
        else if (_currentShape is Shape shape)
        {
            // Handle negative dimensions by adjusting position
            var left = Math.Min(_startPoint.X, currentPoint.X);
            var top = Math.Min(_startPoint.Y, currentPoint.Y);
            var actualWidth = Math.Abs(width);
            var actualHeight = Math.Abs(height);

            Canvas.SetLeft(shape, left);
            Canvas.SetTop(shape, top);

            if (shape is Rectangle rect)
            {
                rect.Width = actualWidth;
                rect.Height = actualHeight;
            }
            else if (shape is Ellipse ellipse)
            {
                ellipse.Width = actualWidth;
                ellipse.Height = actualHeight;
            }
        }
    }

    private void RefreshAnnotationDisplay()
    {
        // Clear existing annotation visuals
        var toRemove = AnnotationCanvas.Children.ToList();
        foreach (var child in toRemove)
        {
            AnnotationCanvas.Children.Remove(child);
        }

        // Draw all annotations for current page
        foreach (var annotation in ViewModel.CurrentAnnotations)
        {
            var visual = CreateAnnotationVisual(annotation);
            if (visual != null)
            {
                Canvas.SetLeft(visual, annotation.X);
                Canvas.SetTop(visual, annotation.Y);
                AnnotationCanvas.Children.Add(visual);
            }
        }
    }

    private UIElement? CreateAnnotationVisual(AnnotationViewModel annotation)
    {
        var color = ParseColor(annotation.Color);
        var brush = new SolidColorBrush(color);

        return annotation.TypeDisplay switch
        {
            "Rectangle" => new Rectangle
            {
                Stroke = brush,
                StrokeThickness = ViewModel.StrokeWidth,
                Width = Math.Abs(annotation.Width),
                Height = Math.Abs(annotation.Height)
            },
            "Ellipse" => new Ellipse
            {
                Stroke = brush,
                StrokeThickness = ViewModel.StrokeWidth,
                Width = Math.Abs(annotation.Width),
                Height = Math.Abs(annotation.Height)
            },
            "Line" => new Line
            {
                Stroke = brush,
                StrokeThickness = ViewModel.StrokeWidth,
                X1 = 0,
                Y1 = 0,
                X2 = annotation.Width,
                Y2 = annotation.Height
            },
            "Arrow" => CreateArrowVisual(annotation, brush),
            "Highlight" => new Rectangle
            {
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(
                    (byte)(ViewModel.AnnotationOpacity * 255),
                    color.R, color.G, color.B)),
                Width = Math.Abs(annotation.Width),
                Height = Math.Abs(annotation.Height)
            },
            "Text" => new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(128, 255, 255, 255)),
                Child = new TextBlock
                {
                    Foreground = brush,
                    FontSize = ViewModel.FontSize,
                    Text = ViewModel.AnnotationText
                }
            },
            _ => null
        };
    }

    private UIElement CreateArrowVisual(AnnotationViewModel annotation, SolidColorBrush brush)
    {
        var canvas = new Canvas
        {
            Width = Math.Max(Math.Abs(annotation.Width), 20),
            Height = Math.Max(Math.Abs(annotation.Height), 20)
        };

        // Line
        var line = new Line
        {
            Stroke = brush,
            StrokeThickness = ViewModel.StrokeWidth,
            X1 = 0,
            Y1 = 0,
            X2 = annotation.Width,
            Y2 = annotation.Height
        };
        canvas.Children.Add(line);

        // Arrowhead
        var angle = Math.Atan2(annotation.Height, annotation.Width);
        var arrowLength = 10;
        var arrowAngle = Math.PI / 6;

        var x1 = annotation.Width - arrowLength * Math.Cos(angle - arrowAngle);
        var y1 = annotation.Height - arrowLength * Math.Sin(angle - arrowAngle);
        var x2 = annotation.Width - arrowLength * Math.Cos(angle + arrowAngle);
        var y2 = annotation.Height - arrowLength * Math.Sin(angle + arrowAngle);

        var arrow1 = new Line
        {
            Stroke = brush,
            StrokeThickness = ViewModel.StrokeWidth,
            X1 = annotation.Width,
            Y1 = annotation.Height,
            X2 = x1,
            Y2 = y1
        };
        canvas.Children.Add(arrow1);

        var arrow2 = new Line
        {
            Stroke = brush,
            StrokeThickness = ViewModel.StrokeWidth,
            X1 = annotation.Width,
            Y1 = annotation.Height,
            X2 = x2,
            Y2 = y2
        };
        canvas.Children.Add(arrow2);

        return canvas;
    }

    private static Windows.UI.Color ParseColor(string colorString)
    {
        try
        {
            if (colorString.StartsWith("#") && colorString.Length == 7)
            {
                var r = Convert.ToByte(colorString.Substring(1, 2), 16);
                var g = Convert.ToByte(colorString.Substring(3, 2), 16);
                var b = Convert.ToByte(colorString.Substring(5, 2), 16);
                return Windows.UI.Color.FromArgb(255, r, g, b);
            }
        }
        catch { }

        return Colors.Red;
    }
}
