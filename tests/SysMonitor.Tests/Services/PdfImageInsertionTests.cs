using FluentAssertions;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// An image the user inserts has to reach the saved file. Every one of these puts a solid red picture on a
/// page, saves, and looks for red pixels in the result — because the failure this guards against is silent:
/// the draw throws, a grey box captioned "[Image]" is painted instead, and the save still reports success.
/// Checking the returned <see cref="PdfOperationResult.Success"/> would not have caught it.
/// </summary>
public class PdfImageInsertionTests : IDisposable
{
    private const string PlaceholderCaption = "[Image]";

    private readonly TempDirectory _temp = new("pdf-images");
    private readonly PdfEditor _editor = new();
    private readonly PdfTools _tools = new();

    public void Dispose() => _temp.Dispose();

    // ---------------------------------------------------------------- editor: image annotation

    [Fact]
    public async Task AnInsertedImage_IsDrawnIntoTheSavedPage()
    {
        var document = await OpenAsync();
        document.Annotations.Add(new ImageAnnotation
        {
            PageId = document.Pages[0].Id,
            ImageData = TestImage.SolidRedPng(),
            X = 100, Y = 100, Width = 120, Height = 120,
        });

        var saved = await SaveAsync(document, "inserted-image.pdf");

        var rendered = await RenderedPage.RenderAsync(saved);
        rendered.Count(TestImage.IsInsertedImage).Should().BeGreaterThan(1000, "the picture itself is on the page");
    }

    [Fact]
    public async Task AnInsertedImage_DoesNotFallBackToThePlaceholderBox()
    {
        var document = await OpenAsync();
        document.Annotations.Add(new ImageAnnotation
        {
            PageId = document.Pages[0].Id,
            ImageData = TestImage.SolidRedPng(),
            X = 100, Y = 100, Width = 120, Height = 120,
        });

        var saved = await SaveAsync(document, "no-placeholder.pdf");

        PdfContent.TextOn(saved, 1).Should().NotContain(
            PlaceholderCaption, "the caption is what the page says when the image failed to load");
    }

    [Fact]
    public async Task AnInsertedJpeg_IsDrawnIntoTheSavedPage()
    {
        var document = await OpenAsync();
        document.Annotations.Add(new ImageAnnotation
        {
            PageId = document.Pages[0].Id,
            ImageData = TestImage.SolidRedJpeg(),
            ImageFormat = "jpeg",
            X = 100, Y = 100, Width = 120, Height = 120,
        });

        var saved = await SaveAsync(document, "inserted-jpeg.pdf");

        var rendered = await RenderedPage.RenderAsync(saved);
        rendered.Count(TestImage.IsInsertedImage).Should().BeGreaterThan(1000, "the format the user picked is not the editor's choice");
    }

    // ---------------------------------------------------------------- editor: signature image

    [Fact]
    public async Task ASignatureImage_IsDrawnIntoTheSavedPage()
    {
        var document = await OpenAsync();
        document.Annotations.Add(new SignatureAnnotation
        {
            PageId = document.Pages[0].Id,
            SignatureImageData = TestImage.SolidRedPng(),
            X = 100, Y = 400, Width = 160, Height = 80,
        });

        var saved = await SaveAsync(document, "signature-image.pdf");

        var rendered = await RenderedPage.RenderAsync(saved);
        rendered.Count(TestImage.IsInsertedImage).Should().BeGreaterThan(1000, "a signature saved as a picture is still a picture");
    }

    // ---------------------------------------------------------------- editor: image watermark

    [Fact]
    public async Task AnImageWatermark_IsDrawnIntoTheSavedPage()
    {
        var document = await OpenAsync();
        document.Annotations.Add(new WatermarkAnnotation
        {
            PageId = document.Pages[0].Id,
            Type = WatermarkType.Image,
            ImageData = TestImage.SolidRedPng(),
            Rotation = 0,
            Width = 200, Height = 200,
            Position = WatermarkPosition.Center,
        });

        var saved = await SaveAsync(document, "image-watermark.pdf");

        var rendered = await RenderedPage.RenderAsync(saved);
        rendered.Count(TestImage.IsInsertedImage).Should().BeGreaterThan(1000, "an image watermark shows the image");
    }

    // ---------------------------------------------------------------- tools: AddSignatureAsync

    [Fact]
    public async Task TheSignatureTool_DrawsTheSignatureIntoTheSavedPage()
    {
        var source = Pdf("to-sign.pdf");
        var output = Path.Combine(_temp.Path, "signed.pdf");

        var result = await _tools.AddSignatureAsync(source, output, new SignatureOptions
        {
            SignatureImageBytes = TestImage.SolidRedPng(),
            PageNumber = 1,
            X = 10, Y = 50, Width = 150,
            SignerName = "A. Tester",
        });

        result.Success.Should().BeTrue(result.ErrorMessage);

        var rendered = await RenderedPage.RenderAsync(output);
        rendered.Count(TestImage.IsInsertedImage).Should().BeGreaterThan(1000, "the signature image reaches the signed file");
    }

    // ---------------------------------------------------------------- honesty about failure

    [Fact]
    public async Task AnImageTheDecoderCannotRead_MakesTheSaveSayItFailed()
    {
        var document = await OpenAsync();
        document.Annotations.Add(new ImageAnnotation
        {
            PageId = document.Pages[0].Id,
            ImageData = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07],
            X = 100, Y = 100, Width = 120, Height = 120,
        });

        var result = await _editor.SavePdfAsync(document, Path.Combine(_temp.Path, "unreadable.pdf"));

        result.Success.Should().BeFalse("a page that could not be drawn as asked is not a successful save");
        result.ErrorMessage.Should().NotBeNullOrWhiteSpace();
    }

    // ---------------------------------------------------------------- helpers

    private string Pdf(string name)
    {
        var path = Path.Combine(_temp.Path, name);
        PdfTestFile.WriteNumberedPages(path, pageCount: 1);
        return path;
    }

    private async Task<PdfEditorDocument> OpenAsync()
    {
        var document = await _editor.OpenPdfAsync(Pdf("input.pdf"));
        document.Should().NotBeNull();
        return document!;
    }

    private async Task<string> SaveAsync(PdfEditorDocument document, string name)
    {
        var path = Path.Combine(_temp.Path, name);
        var result = await _editor.SavePdfAsync(document, path);
        result.Success.Should().BeTrue(result.ErrorMessage);
        return path;
    }
}
