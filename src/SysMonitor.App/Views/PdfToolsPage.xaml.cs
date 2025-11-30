using Microsoft.Graphics.Canvas;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SysMonitor.App.ViewModels;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Input.Inking;

namespace SysMonitor.App.Views;

public sealed partial class PdfToolsPage : Page
{
    public PdfToolsViewModel ViewModel { get; }

    public PdfToolsPage()
    {
        ViewModel = App.GetService<PdfToolsViewModel>();
        InitializeComponent();
        // Set DataContext for {Binding} expressions inside DataTemplates
        DataContext = ViewModel;

        // Initialize InkCanvas when loaded
        Loaded += PdfToolsPage_Loaded;
    }

    private void PdfToolsPage_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeInkCanvas();
    }

    private void InitializeInkCanvas()
    {
        // Configure the InkCanvas for mouse and touch input
        var inkPresenter = SignatureInkCanvas.InkPresenter;

        // Enable mouse and touch input (pen is enabled by default)
        inkPresenter.InputDeviceTypes = CoreInputDeviceTypes.Mouse |
                                        CoreInputDeviceTypes.Touch |
                                        CoreInputDeviceTypes.Pen;

        // Set default drawing attributes for signature
        var drawingAttributes = inkPresenter.CopyDefaultDrawingAttributes();
        drawingAttributes.Color = Colors.Black;
        drawingAttributes.Size = new Size(2, 2);
        drawingAttributes.IgnorePressure = false;
        drawingAttributes.FitToCurve = true;
        inkPresenter.UpdateDefaultDrawingAttributes(drawingAttributes);

        // Update ViewModel when strokes change
        inkPresenter.StrokesCollected += InkPresenter_StrokesCollected;
        inkPresenter.StrokesErased += InkPresenter_StrokesErased;
    }

    private async void InkPresenter_StrokesCollected(InkPresenter sender, InkStrokesCollectedEventArgs args)
    {
        await CaptureSignatureToViewModelAsync();
    }

    private async void InkPresenter_StrokesErased(InkPresenter sender, InkStrokesErasedEventArgs args)
    {
        await CaptureSignatureToViewModelAsync();
    }

    private async Task CaptureSignatureToViewModelAsync()
    {
        try
        {
            var strokes = SignatureInkCanvas.InkPresenter.StrokeContainer.GetStrokes();
            if (strokes.Count == 0)
            {
                ViewModel.SetDrawnSignatureBytes(null);
                return;
            }

            // Render strokes to PNG bytes
            var bytes = await RenderInkToPngAsync();
            ViewModel.SetDrawnSignatureBytes(bytes);
        }
        catch
        {
            ViewModel.SetDrawnSignatureBytes(null);
        }
    }

    private async Task<byte[]?> RenderInkToPngAsync()
    {
        var strokeContainer = SignatureInkCanvas.InkPresenter.StrokeContainer;
        var strokes = strokeContainer.GetStrokes();

        if (strokes.Count == 0)
            return null;

        // Get the bounding box of all strokes
        var bounds = strokeContainer.BoundingRect;
        if (bounds.Width == 0 || bounds.Height == 0)
            return null;

        // Add some padding
        const int padding = 10;
        var width = (int)bounds.Width + (padding * 2);
        var height = (int)bounds.Height + (padding * 2);

        // Create a CanvasDevice for rendering
        using var device = CanvasDevice.GetSharedDevice();
        using var renderTarget = new CanvasRenderTarget(device, width, height, 96);

        // Draw strokes onto the render target with transparent background
        using (var session = renderTarget.CreateDrawingSession())
        {
            // Transparent background
            session.Clear(Colors.Transparent);

            // Offset strokes to account for bounding box position and padding
            var offsetX = (float)(-bounds.X + padding);
            var offsetY = (float)(-bounds.Y + padding);

            // Draw each stroke
            foreach (var stroke in strokes)
            {
                var inkPoints = stroke.GetInkPoints();
                if (inkPoints.Count < 2) continue;

                var points = new List<System.Numerics.Vector2>();
                foreach (var point in inkPoints)
                {
                    points.Add(new System.Numerics.Vector2(
                        (float)point.Position.X + offsetX,
                        (float)point.Position.Y + offsetY));
                }

                // Draw as connected lines
                for (int i = 0; i < points.Count - 1; i++)
                {
                    session.DrawLine(points[i], points[i + 1], Colors.Black, 2);
                }
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

        return bytes;
    }

    private void ClearInkCanvas_Click(object sender, RoutedEventArgs e)
    {
        SignatureInkCanvas.InkPresenter.StrokeContainer.Clear();
        ViewModel.ClearDrawnSignature();
    }
}
