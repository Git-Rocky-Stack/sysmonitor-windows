using FluentAssertions;
using PdfSharp.Pdf.IO;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// A redaction has to remove what is under it, not hide it: the text must be gone from the saved file, where
/// selecting, copying or extracting cannot bring it back. Everything else on the page has to survive.
/// </summary>
public class PdfRedactionTests : IDisposable
{
    private const string Secret = "CONFIDENTIAL 12345";

    private readonly TempDirectory _temp = new("pdf-redaction");
    private readonly PdfEditor _editor = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task RedactedText_IsGoneFromTheSavedFile()
    {
        var source = NewPath("source");
        PdfTestFile.WritePagesWithSecretText(source, pageCount: 2, Secret);

        var saved = await RedactTheSecretAsync(source, overlayText: "");

        PdfContent.TextOn(saved, 1).Should().BeEmpty("the page holds no text at all any more");
        PdfContent.TextOn(saved, 2).Should().Contain(text => text.Contains(Secret), "pages without a redaction are untouched");
        PdfContent.FontCountOn(saved, 1).Should().Be(0, "a page with no text needs no fonts");
        PdfContent.ImageCountOn(saved, 1).Should().Be(1, "the page is saved as a picture of itself");
    }

    [Fact]
    public async Task RedactedText_IsNotVisibleEitherAndTheRestOfThePageIsUnchanged()
    {
        var source = NewPath("source");
        PdfTestFile.WritePagesWithSecretText(source, pageCount: 1, Secret);
        var before = await RenderedPage.RenderAsync(source);

        var saved = await RedactTheSecretAsync(source, overlayText: "");

        var after = await RenderedPage.RenderAsync(saved);
        after.Count(RenderedPage.IsMark).Should().Be(0, "the text is covered as well as removed");

        after.Width.Should().Be(before.Width, "the page is the same size as before");
        after.Height.Should().Be(before.Height);

        var keepBefore = before.BoundsOf(PdfTestFile.IsKeep);
        var keepAfter = after.BoundsOf(PdfTestFile.IsKeep);
        keepAfter.Should().NotBeNull("the rest of the page is still there");
        keepAfter!.Value.Left.Should().BeCloseTo(keepBefore!.Value.Left, 3, "and has not moved or resized");
        keepAfter.Value.Top.Should().BeCloseTo(keepBefore.Value.Top, 3);
        keepAfter.Value.Width.Should().BeCloseTo(keepBefore.Value.Width, 3);
        keepAfter.Value.Height.Should().BeCloseTo(keepBefore.Value.Height, 3);
    }

    [Fact]
    public async Task ARedactionCanStillCarryItsLabel()
    {
        var source = NewPath("source");
        PdfTestFile.WritePagesWithSecretText(source, pageCount: 1, Secret);

        var saved = await RedactTheSecretAsync(source, overlayText: "REDACTED");

        var text = PdfContent.TextOn(saved, 1);
        text.Should().NotBeEmpty();
        string.Concat(text).Should().Be("REDACTED", "the label is the only text left on the page");
    }

    [Fact]
    public async Task RedactingWorksOnAPageTheFileTurnsOnItsSide()
    {
        var source = NewPath("rotated");
        PdfTestFile.WritePagesWithSecretText(source, pageCount: 1, Secret, rotate: 90);

        var saved = await RedactTheSecretAsync(source, overlayText: "");

        PdfContent.TextOn(saved, 1).Should().BeEmpty();
        var rendered = await RenderedPage.RenderAsync(saved);
        rendered.Width.Should().BeGreaterThan(rendered.Height, "the page is still shown on its side");
        rendered.Count(RenderedPage.IsMark).Should().Be(0);
        rendered.Count(PdfTestFile.IsKeep).Should().BeGreaterThan(1000);
    }

    [Fact]
    public async Task ARedactedPageKeepsItsSizeAndFollowsTheUsersRotation()
    {
        var source = NewPath("source");
        PdfTestFile.WritePagesWithSecretText(source, pageCount: 1, Secret);

        var saved = await RedactTheSecretAsync(source, overlayText: "", userRotation: 90);

        using (var written = PdfReader.Open(saved, PdfDocumentOpenMode.Import))
        {
            var page = written.Pages[0];
            page.Rotate.Should().Be(90);
            page.MediaBox.Width.Should().BeApproximately(612, 0.5, "the page is the size it always was");
            page.MediaBox.Height.Should().BeApproximately(792, 0.5);
        }

        var rendered = await RenderedPage.RenderAsync(saved);
        rendered.Width.Should().BeGreaterThan(rendered.Height, "the reader shows it turned");
    }

    [Fact]
    public async Task ARedactionOnAnInsertedBlankPage_IsDrawnWithoutTouchingTheOtherPages()
    {
        var source = NewPath("source");
        PdfTestFile.WritePagesWithSecretText(source, pageCount: 1, Secret);

        var document = await _editor.OpenPdfAsync(source);
        (await _editor.InsertBlankPageAsync(document!, 1)).Success.Should().BeTrue();
        (await _editor.AddRedactionAsync(document!, 2, new RedactionAnnotation
        {
            X = 100,
            Y = 100,
            Width = 200,
            Height = 80,
            FillColor = "#000000",
            OverlayText = "",
            CoordinateScale = PdfPageGeometry.CanvasCoordinateScale(1.0),
        })).Success.Should().BeTrue();

        var saved = NewPath("saved");
        var result = await _editor.SavePdfAsync(document!, saved);
        result.Success.Should().BeTrue(result.ErrorMessage);

        var blank = await RenderedPage.RenderAsync(saved, 2);
        blank.BoundsOf(RenderedPage.IsInk).Should().NotBeNull("the box is on the blank page");
        PdfContent.TextOn(saved, 1).Should().Contain(text => text.Contains(Secret), "the untouched page keeps its text");
    }

    /// <summary>
    /// Finds the secret text on the page as the editor shows it, covers exactly that with a redaction, and saves.
    /// </summary>
    private async Task<string> RedactTheSecretAsync(string source, string overlayText, int userRotation = 0)
    {
        var canvas = await RenderedPage.RenderAsync(source);
        var text = canvas.BoundsOf(RenderedPage.IsMark);
        text.Should().NotBeNull("the page shows the secret in blue");

        var document = await _editor.OpenPdfAsync(source);
        document.Should().NotBeNull();
        var page = document!.Pages[0];
        page.Rotation = PdfPageGeometry.NormalizeRotation(page.Rotation + userRotation);

        var box = text!.Value.Expand(3);
        var added = await _editor.AddRedactionAsync(document, 1, new RedactionAnnotation
        {
            X = box.Left,
            Y = box.Top,
            Width = box.Width,
            Height = box.Height,
            FillColor = "#000000",
            OverlayText = overlayText,
            CoordinateScale = PdfPageGeometry.CanvasCoordinateScale(1.0),
        });
        added.Success.Should().BeTrue(added.ErrorMessage);

        var saved = NewPath("saved");
        var result = await _editor.SavePdfAsync(document, saved);
        result.Success.Should().BeTrue(result.ErrorMessage);
        return saved;
    }

    private string NewPath(string name) => Path.Combine(_temp.Path, $"{name}-{Guid.NewGuid():N}.pdf");
}
