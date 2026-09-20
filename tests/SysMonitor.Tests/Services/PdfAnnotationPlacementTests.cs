using FluentAssertions;
using PdfSharp.Pdf.IO;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// What the editor shows is what the saved file holds: a box drawn over something on the page covers that
/// same thing in the output, whatever the zoom, the page's rotation in the file, its CropBox, or a rotation
/// the user applies afterwards. Each test finds a blue mark on the rendered page, covers exactly it, and
/// checks the saved page.
/// </summary>
public class PdfAnnotationPlacementTests : IDisposable
{
    private readonly TempDirectory _temp = new("pdf-annotations");
    private readonly PdfEditor _editor = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    // rotation in the file, zoom the box was drawn at, rotation the user applies afterwards
    [InlineData(0, 1.0, 0)]
    [InlineData(0, 1.75, 0)]
    [InlineData(90, 1.0, 0)]
    [InlineData(90, 0.5, 0)]
    [InlineData(180, 1.25, 0)]
    [InlineData(270, 1.5, 0)]
    [InlineData(0, 1.0, 90)]
    [InlineData(90, 1.0, 180)]
    [InlineData(270, 1.0, 90)]
    [InlineData(90, 1.0, -90)]
    public async Task BoxDrawnOverTheMark_CoversItInTheSavedFile(int fileRotation, double zoom, int userRotation)
    {
        var source = NewPath("source");
        PdfTestFile.WritePageWithMark(source, PdfTestFile.Letter, rotate: fileRotation);

        await AnnotateAndCheckAsync(source, zoom, userRotation);
    }

    [Fact]
    public async Task BoxDrawnOnACroppedPageTheFileRotates_CoversTheMark()
    {
        var source = NewPath("cropped");
        PdfTestFile.WritePageWithMark(source, PdfTestFile.Letter, cropBox: [36, 72, 576, 756], rotate: 90);

        await AnnotateAndCheckAsync(source, zoom: 1.0, userRotation: 0);
    }

    [Fact]
    public async Task BoxDrawnOnAPageWhoseMediaBoxIsNotAtTheOrigin_CoversTheMark()
    {
        var source = NewPath("offset-mediabox");
        PdfTestFile.WritePageWithMark(source, [100, 50, 712, 842]);

        await AnnotateAndCheckAsync(source, zoom: 1.0, userRotation: 0);
    }

    [Fact]
    public async Task BoxDrawnOnAPageThatInheritsItsRotationAndCropBox_CoversTheMark()
    {
        var source = NewPath("inherited");
        PdfTestFile.WritePageWithInheritedAttributes(source);

        await AnnotateAndCheckAsync(source, zoom: 1.0, userRotation: 0);
    }

    [Fact]
    public async Task TurningARotatedPageBackToUpright_ClearsTheRotationInTheSavedFile()
    {
        var source = NewPath("rotated");
        PdfTestFile.WritePageWithMark(source, PdfTestFile.Letter, rotate: 90);

        var document = await _editor.OpenPdfAsync(source);
        document!.Pages[0].OriginalRotation.Should().Be(90);
        document.Pages[0].Rotation = 0;

        var saved = NewPath("saved");
        var saveResult = await _editor.SavePdfAsync(document, saved);
        saveResult.Success.Should().BeTrue(saveResult.ErrorMessage);

        using (var written = PdfReader.Open(saved, PdfDocumentOpenMode.Import))
            written.Pages[0].Rotate.Should().Be(0);

        var rendered = await RenderedPage.RenderAsync(saved);
        rendered.Width.Should().BeLessThan(rendered.Height, "the page stands upright again");
    }

    [Fact]
    public async Task TextAnnotation_StartsAtItsTopLeftCorner()
    {
        var source = NewPath("blank");
        PdfTestFile.WriteBlankPage(source, PdfTestFile.Letter);

        const double fontSize = 40;
        const double x = 100;
        const double y = 200;
        var document = await _editor.OpenPdfAsync(source);
        await _editor.AddTextAnnotationAsync(document!, 1, new TextAnnotation
        {
            X = x,
            Y = y,
            Width = 300,
            Height = 60,
            Text = "HHHH",
            FontSize = fontSize,
            FontFamily = "Arial",
            Color = "#000000",
            CoordinateScale = PdfPageGeometry.CanvasCoordinateScale(1.0),
        });

        // At zoom 1 a canvas pixel is a rendered pixel again, so the text is expected where it was placed.
        var (_, text) = await SaveAndFindInkAsync(document!);
        text.Left.Should().BeCloseTo((int)x, 4);
        text.Top.Should().BeGreaterThanOrEqualTo((int)y - 1, "the text hangs below its top-left corner, never above it");
        text.Top.Should().BeLessThan((int)(y + (0.45 * fontSize)), "capitals start just below that corner");
        text.Bottom.Should().BeLessThan((int)(y + (1.2 * fontSize)), "and one line fits in a line's height");
    }

    [Fact]
    public async Task Watermark_RunsAlongThePageAsDisplayedAndSitsInItsMiddle()
    {
        var source = NewPath("landscape");
        PdfTestFile.WriteBlankPage(source, PdfTestFile.Letter, rotate: 90);

        var document = await _editor.OpenPdfAsync(source);
        await _editor.AddWatermarkAsync(document!, Watermark("HHHHHHHH", 48));

        var (rendered, ink) = await SaveAndFindInkAsync(document!);
        ink.Width.Should().BeGreaterThan(ink.Height * 2, "the text runs along the page the reader sees");
        ink.CenterX.Should().BeApproximately(rendered.Width / 2.0, 8, "and sits in the middle of it");
        ink.CenterY.Should().BeApproximately(rendered.Height / 2.0, 8);
    }

    [Fact]
    public async Task Watermark_IsCentredOnTheVisiblePageWhenTheCropBoxIsSmaller()
    {
        var source = NewPath("half-cropped");
        PdfTestFile.WriteBlankPage(source, PdfTestFile.Letter, cropBox: [0, 0, 306, 792]);

        var document = await _editor.OpenPdfAsync(source);
        await _editor.AddWatermarkAsync(document!, Watermark("HHHH", 24));

        var (rendered, ink) = await SaveAndFindInkAsync(document!);

        ink.CenterX.Should().BeApproximately(rendered.Width / 2.0, 8, "the watermark sits in the middle of the page the reader sees");
        ink.CenterY.Should().BeApproximately(rendered.Height / 2.0, 8);
        ink.Right.Should().BeLessThan(rendered.Width - 1, "the whole watermark is inside the visible page");
    }

    [Fact]
    public async Task SaveRefusesAnAnnotationWhoseCoordinateScaleIsUnusable()
    {
        var source = NewPath("blank");
        PdfTestFile.WriteBlankPage(source, PdfTestFile.Letter);

        var document = await _editor.OpenPdfAsync(source);
        await _editor.AddHighlightAsync(document!, 1, new HighlightAnnotation
        {
            X = 10,
            Y = 10,
            Width = 50,
            Height = 20,
            CoordinateScale = 0,
        });

        var saved = NewPath("saved");
        var result = await _editor.SavePdfAsync(document!, saved);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("coordinate scale");
        File.Exists(saved).Should().BeFalse("a save that cannot place an annotation writes nothing");
    }

    [Fact]
    public async Task SavingReportsSuccessAndTheNumberOfPagesWritten()
    {
        var source = NewPath("two-pages");
        PdfTestFile.WriteBlankPage(source, PdfTestFile.Letter);
        var document = await _editor.OpenPdfAsync(source);
        await _editor.DuplicatePageAsync(document!, 1);

        var saved = NewPath("saved");
        var result = await _editor.SavePdfAsync(document!, saved);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.PagesProcessed.Should().Be(2);
        result.OutputPath.Should().Be(saved);
        File.Exists(saved).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertingAnImageToPdfReportsSuccessAndTheNumberOfPagesWritten()
    {
        var image = Path.Combine(_temp.Path, "picture.png");
        using (var bitmap = new System.Drawing.Bitmap(40, 30))
        {
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.Clear(System.Drawing.Color.CornflowerBlue);
            bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Png);
        }

        var saved = NewPath("converted");
        var result = await new PdfTools().ConvertToPdfAsync(image, saved);

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.PagesProcessed.Should().Be(1);
        File.Exists(saved).Should().BeTrue();
    }

    [Theory]
    [InlineData(90, 90, 0)]
    [InlineData(180, 90, 90)]
    [InlineData(0, 270, 90)]
    [InlineData(0, 0, 0)]
    [InlineData(90, 180, 270)]
    public void PreviewRotation_TurnsTheRenderedPageTheRestOfTheWay(int rotation, int originalRotation, int expected) =>
        new PdfPageInfo { Rotation = rotation, OriginalRotation = originalRotation }.PreviewRotation.Should().Be(expected);

    [Fact]
    public void CanvasCoordinateScale_TurnsCanvasPixelsIntoPoints()
    {
        PdfPageGeometry.CanvasCoordinateScale(1.0).Should().Be(0.75);
        PdfPageGeometry.CanvasCoordinateScale(2.0).Should().Be(0.375);
        FluentActions.Invoking(() => PdfPageGeometry.CanvasCoordinateScale(0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Covers the mark on the page as the canvas hands coordinates over, saves, and checks the saved page:
    /// the mark is gone, and the box that hides it is where and how big it should be.
    /// </summary>
    private async Task AnnotateAndCheckAsync(string source, double zoom, int userRotation)
    {
        var canvas = await RenderedPage.RenderAsync(source, 1, zoom);
        var mark = canvas.BoundsOf(RenderedPage.IsMark);
        mark.Should().NotBeNull("the page holds a mark to draw over");

        var document = await _editor.OpenPdfAsync(source);
        document.Should().NotBeNull();
        var page = document!.Pages[0];
        page.Rotation = PdfPageGeometry.NormalizeRotation(page.Rotation + userRotation);

        var box = mark!.Value.Expand(2);
        await _editor.AddHighlightAsync(document, 1, new HighlightAnnotation
        {
            X = box.Left,
            Y = box.Top,
            Width = box.Width,
            Height = box.Height,
            Color = "#000000",
            Opacity = 1.0,
            CoordinateScale = PdfPageGeometry.CanvasCoordinateScale(zoom),
        });

        var saved = NewPath("saved");
        var result = await _editor.SavePdfAsync(document, saved);
        result.Success.Should().BeTrue(result.ErrorMessage);

        var rendered = await RenderedPage.RenderAsync(saved, 1, zoom);
        rendered.Count(RenderedPage.IsMark).Should().Be(0, "the box was drawn over the mark");

        var ink = rendered.BoundsOf(RenderedPage.IsInk);
        ink.Should().NotBeNull("the box is on the page");

        var expected = box.Rotate(userRotation, canvas.Width, canvas.Height);
        ink!.Value.Left.Should().BeCloseTo(expected.Left, 3);
        ink.Value.Top.Should().BeCloseTo(expected.Top, 3);
        ink.Value.Width.Should().BeCloseTo(expected.Width, 3, "the box covers the mark, not the page");
        ink.Value.Height.Should().BeCloseTo(expected.Height, 3);
    }

    private static WatermarkAnnotation Watermark(string text, double fontSize) => new()
    {
        PageNumber = 1,
        Text = text,
        FontSize = fontSize,
        Rotation = 0,
        Position = WatermarkPosition.Center,
        Opacity = 1.0,
        Color = "#000000",
        ApplyToAllPages = false,
    };

    private async Task<(RenderedPage Page, PixelBox Ink)> SaveAndFindInkAsync(PdfEditorDocument document, double zoom = 1.0)
    {
        var saved = NewPath("saved");
        var result = await _editor.SavePdfAsync(document, saved);
        result.Success.Should().BeTrue(result.ErrorMessage);

        var rendered = await RenderedPage.RenderAsync(saved, 1, zoom);
        var ink = rendered.BoundsOf(RenderedPage.IsInk);
        ink.Should().NotBeNull("the annotation is on the page");
        return (rendered, ink!.Value);
    }

    private string NewPath(string name) => Path.Combine(_temp.Path, $"{name}-{Guid.NewGuid():N}.pdf");
}
