using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using SysMonitor.App.ViewModels;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SysMonitor.App.Views;

public sealed partial class PdfToolsPage : Page
{
    public PdfToolsViewModel ViewModel { get; }

    private bool _isDrawing;
    private Point _lastPoint;
    private readonly List<Line> _drawnLines = new();

    public PdfToolsPage()
    {
        ViewModel = App.GetService<PdfToolsViewModel>();
        InitializeComponent();
        // Set DataContext for {Binding} expressions inside DataTemplates
        DataContext = ViewModel;
    }

    private void SignatureCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        _isDrawing = true;
        _lastPoint = e.GetCurrentPoint(SignatureCanvas).Position;
        SignatureCanvas.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    private void SignatureCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing) return;

        var currentPoint = e.GetCurrentPoint(SignatureCanvas).Position;

        // Draw a line from last point to current point
        var line = new Line
        {
            X1 = _lastPoint.X,
            Y1 = _lastPoint.Y,
            X2 = currentPoint.X,
            Y2 = currentPoint.Y,
            Stroke = new SolidColorBrush(Colors.Black),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };

        SignatureCanvas.Children.Add(line);
        _drawnLines.Add(line);
        _lastPoint = currentPoint;
        e.Handled = true;

        // Update ViewModel that we have a drawn signature
        ViewModel.SetDrawnSignatureBytes(Array.Empty<byte>()); // Placeholder until capture
    }

    private async void SignatureCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDrawing) return;

        _isDrawing = false;
        SignatureCanvas.ReleasePointerCapture(e.Pointer);
        e.Handled = true;

        // Capture the signature when pointer is released
        if (_drawnLines.Count > 0)
        {
            await CaptureSignatureAsync();
        }
    }

    private async Task CaptureSignatureAsync()
    {
        try
        {
            if (_drawnLines.Count == 0)
            {
                ViewModel.SetDrawnSignatureBytes(null);
                return;
            }

            // Calculate bounds of the drawing
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var line in _drawnLines)
            {
                minX = Math.Min(minX, Math.Min(line.X1, line.X2));
                minY = Math.Min(minY, Math.Min(line.Y1, line.Y2));
                maxX = Math.Max(maxX, Math.Max(line.X1, line.X2));
                maxY = Math.Max(maxY, Math.Max(line.Y1, line.Y2));
            }

            // Add padding
            const int padding = 10;
            var width = (int)(maxX - minX) + (padding * 2);
            var height = (int)(maxY - minY) + (padding * 2);

            if (width <= padding * 2 || height <= padding * 2)
            {
                ViewModel.SetDrawnSignatureBytes(null);
                return;
            }

            // Use Win2D to render the signature
            using var device = CanvasDevice.GetSharedDevice();
            using var renderTarget = new CanvasRenderTarget(device, width, height, 96);

            using (var session = renderTarget.CreateDrawingSession())
            {
                // Transparent background
                session.Clear(Colors.Transparent);

                // Draw each line with offset
                foreach (var line in _drawnLines)
                {
                    var x1 = (float)(line.X1 - minX + padding);
                    var y1 = (float)(line.Y1 - minY + padding);
                    var x2 = (float)(line.X2 - minX + padding);
                    var y2 = (float)(line.Y2 - minY + padding);

                    session.DrawLine(x1, y1, x2, y2, Colors.Black, 2);
                }
            }

            // Save to PNG bytes
            using var stream = new InMemoryRandomAccessStream();
            await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);

            // Convert to byte array
            var reader = new DataReader(stream.GetInputStreamAt(0));
            var bytes = new byte[stream.Size];
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);

            ViewModel.SetDrawnSignatureBytes(bytes);
        }
        catch
        {
            ViewModel.SetDrawnSignatureBytes(null);
        }
    }

    private void ClearInkCanvas_Click(object sender, RoutedEventArgs e)
    {
        // Clear all drawn lines from the canvas
        foreach (var line in _drawnLines)
        {
            SignatureCanvas.Children.Remove(line);
        }
        _drawnLines.Clear();
        ViewModel.ClearDrawnSignature();
    }

    private async void TypedSignatureInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        var text = ViewModel.TypedSignatureName;

        if (string.IsNullOrWhiteSpace(text))
        {
            ViewModel.SetTypedSignatureBytes(null);
            return;
        }

        try
        {
            // Render the text using a cursive/script font
            await RenderTypedSignatureAsync(text);
        }
        catch
        {
            ViewModel.SetTypedSignatureBytes(null);
        }
    }

    private async Task RenderTypedSignatureAsync(string text)
    {
        try
        {
            using var device = CanvasDevice.GetSharedDevice();

            // Create text format with cursive font
            // Try different script fonts that might be available on Windows
            var textFormat = new CanvasTextFormat
            {
                FontFamily = "Segoe Script",
                FontSize = 48,
                FontWeight = Windows.UI.Text.FontWeights.Normal,
                WordWrapping = CanvasWordWrapping.NoWrap
            };

            // Measure the text to determine canvas size
            using var textLayout = new CanvasTextLayout(device, text, textFormat, 1000, 100);
            var bounds = textLayout.LayoutBounds;

            // Add padding
            const int padding = 20;
            var width = (int)bounds.Width + (padding * 2);
            var height = (int)bounds.Height + (padding * 2);

            if (width <= padding * 2 || height <= padding * 2)
            {
                ViewModel.SetTypedSignatureBytes(null);
                return;
            }

            // Create render target
            using var renderTarget = new CanvasRenderTarget(device, width, height, 96);

            using (var session = renderTarget.CreateDrawingSession())
            {
                // Transparent background
                session.Clear(Colors.Transparent);

                // Draw the text
                session.DrawTextLayout(textLayout, padding, padding, Colors.Black);
            }

            // Save to PNG bytes
            using var stream = new InMemoryRandomAccessStream();
            await renderTarget.SaveAsync(stream, CanvasBitmapFileFormat.Png);

            // Convert to byte array
            var reader = new DataReader(stream.GetInputStreamAt(0));
            var bytes = new byte[stream.Size];
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(bytes);

            ViewModel.SetTypedSignatureBytes(bytes);
        }
        catch
        {
            ViewModel.SetTypedSignatureBytes(null);
        }
    }
}
