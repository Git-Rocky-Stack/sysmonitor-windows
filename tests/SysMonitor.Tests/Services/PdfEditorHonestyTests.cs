using DocumentFormat.OpenXml.Packaging;
using FluentAssertions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// The PDF editor must only claim what it does. Nothing here extracts the text of a page, so the find panel
/// searches annotations and says so, the Word export is a report about the document rather than the document
/// converted, and compression offers only the option it actually applies. These tests hold those limits in
/// place: each one puts real text on the page and proves the feature neither reads it nor pretends to.
/// </summary>
public class PdfEditorHonestyTests : IDisposable
{
    private const string PageText = "Sales grew 14 percent";

    private readonly TempDirectory _temp = new("pdf-honesty");
    private readonly PdfEditor _editor = new();

    public void Dispose() => _temp.Dispose();

    // ---------------------------------------------------------------- find in annotations

    [Fact]
    public async Task Search_FindsTheWordsInAnAnnotation()
    {
        var document = await OpenAsync();
        document.Annotations.Add(Text(document, "Quarterly revenue exceeded forecast"));

        var results = await _editor.SearchAnnotationsAsync(document, "revenue");

        results.Should().ContainSingle();
        results[0].MatchedText.Should().Be("revenue");
        results[0].Preview.Should().Be("Quarterly revenue exceeded forecast");
        results[0].PageNumber.Should().Be(1);
    }

    [Fact]
    public async Task Search_DoesNotReadTheTextOnThePage()
    {
        var path = Pdf("letter.pdf", pageCount: 1);
        PdfContent.TextOn(path, 1).Should().Contain(PageText, "the test is worthless if the page has no text");

        var document = await _editor.OpenPdfAsync(path);
        document.Should().NotBeNull();

        var results = await _editor.SearchAnnotationsAsync(document!, "Sales");

        results.Should().BeEmpty("the page text is not searched, which is why this is not called SearchText");
    }

    [Fact]
    public async Task Search_ReportsTheAnnotationsOwnSpelling_NotTheQuery()
    {
        var document = await OpenAsync();
        document.Annotations.Add(Text(document, "Margin held"));

        var results = await _editor.SearchAnnotationsAsync(document, "MARGIN");

        results.Should().ContainSingle();
        results[0].MatchedText.Should().Be("Margin", "a result shows the annotation's words, not the ones typed");
    }

    [Fact]
    public async Task Search_CanBeCaseSensitive()
    {
        var document = await OpenAsync();
        document.Annotations.Add(Text(document, "Margin held"));

        (await _editor.SearchAnnotationsAsync(document, "margin", caseSensitive: true)).Should().BeEmpty();
        (await _editor.SearchAnnotationsAsync(document, "Margin", caseSensitive: true)).Should().ContainSingle();
    }

    [Fact]
    public async Task Search_TrimsLongTextAroundTheMatch()
    {
        var document = await OpenAsync();
        var padding = new string('x', 60);
        document.Annotations.Add(Text(document, $"{padding} needle {padding}"));

        var results = await _editor.SearchAnnotationsAsync(document, "needle");

        results.Should().ContainSingle();
        results[0].ContextBefore.Should().StartWith("…").And.HaveLength(41);
        results[0].ContextAfter.Should().EndWith("…").And.HaveLength(41);
        results[0].Preview.Should().Contain("needle");
    }

    [Fact]
    public async Task Search_CoversEveryAnnotationThatShowsText()
    {
        var document = await OpenAsync();
        var page = document.Pages[0];
        document.Annotations.AddRange(
        [
            new StickyNoteAnnotation { PageId = page.Id, Title = "Budget", Content = "check the hollandaise" },
            new StampAnnotation { PageId = page.Id, CustomText = "hollandaise approved" },
            new WatermarkAnnotation { PageId = page.Id, Text = "hollandaise draft" },
            new SignatureAnnotation { PageId = page.Id, SignerName = "A. Hollandaise" },
            new LinkAnnotation { PageId = page.Id, DisplayText = "recipe", Url = "https://example.test/hollandaise" },
        ]);

        var results = await _editor.SearchAnnotationsAsync(document, "hollandaise");

        results.Should().HaveCount(5, "every annotation that shows text is searched");
    }

    [Fact]
    public async Task Search_IgnoresAnnotationsThatShowNoText()
    {
        var document = await OpenAsync();
        var page = document.Pages[0];
        document.Annotations.AddRange(
        [
            new HighlightAnnotation { PageId = page.Id },
            new RedactionAnnotation { PageId = page.Id },
            new ShapeAnnotation { PageId = page.Id, Type = ShapeType.Rectangle },
        ]);

        (await _editor.SearchAnnotationsAsync(document, "e")).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_WorksWhenTheFileHasMoved()
    {
        var document = await OpenAsync();
        document.Annotations.Add(Text(document, "still findable"));
        File.Delete(document.FilePath);

        var results = await _editor.SearchAnnotationsAsync(document, "findable");

        results.Should().ContainSingle("the annotations are in memory, so the search does not need the file");
    }

    [Fact]
    public async Task Search_ReportsWhereTheAnnotationIsNow()
    {
        var document = await OpenAsync(pageCount: 3);
        document.Annotations.Add(Text(document, "on the last page", pagePosition: 3));

        (await _editor.ReorderPagesAsync(document, [2, 0, 1])).Success.Should().BeTrue();

        var results = await _editor.SearchAnnotationsAsync(document, "last page");

        results.Should().ContainSingle();
        results[0].PageNumber.Should().Be(1, "that page is now the first one");
    }

    // ---------------------------------------------------------------- the Word report

    [Fact]
    public async Task Report_SaysWhatItDoesAndDoesNotContain()
    {
        var document = await OpenAsync();
        var output = Path.Combine(_temp.Path, "report.docx");

        (await _editor.ExportAnnotationReportAsync(document, output)).Success.Should().BeTrue();

        var text = WordText(output);
        text.Should().Contain("The text of the pages themselves is not included.");
        text.Should().NotContain(PageText, "the report does not carry the page content, and must not imply it does");
    }

    [Fact]
    public async Task Report_ListsThePagesAndTheirAnnotations()
    {
        var document = await OpenAsync(pageCount: 2);
        document.Annotations.Add(Text(document, "fix the footer", pagePosition: 2));
        var output = Path.Combine(_temp.Path, "report.docx");

        (await _editor.ExportAnnotationReportAsync(document, output)).Success.Should().BeTrue();

        var text = WordText(output);
        text.Should().Contain("--- Page 1 (612 x 792 pt) ---");
        text.Should().Contain("--- Page 2 (612 x 792 pt) ---");
        text.Should().Contain("fix the footer");
    }

    [Fact]
    public async Task Report_BreaksBetweenPagesByWhereTheyAreNow()
    {
        var document = await OpenAsync(pageCount: 3);

        // A blank page has no number in the source file, and a reorder moves the rest away from theirs.
        (await _editor.InsertBlankPageAsync(document, 1)).Success.Should().BeTrue();
        var output = Path.Combine(_temp.Path, "report.docx");

        (await _editor.ExportAnnotationReportAsync(document, output)).Success.Should().BeTrue();

        PageBreaks(output).Should().Be(3, "four pages are separated by three breaks");
    }

    // ---------------------------------------------------------------- compression

    [Fact]
    public async Task Compression_ClearsTheMetadataWhenAskedTo()
    {
        var path = Pdf("meta.pdf", pageCount: 1);
        Describe(path, "Quarterly report", "R. Elsalaymeh");
        var output = Path.Combine(_temp.Path, "smaller.pdf");

        var result = await _editor.CompressPdfAsync(path, output, new PdfCompressionOptions { RemoveMetadata = true });

        result.Success.Should().BeTrue();
        using var compressed = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        compressed.Info.Title.Should().BeEmpty();
        compressed.Info.Author.Should().BeEmpty();
    }

    [Fact]
    public async Task Compression_KeepsTheMetadataByDefault()
    {
        var path = Pdf("meta.pdf", pageCount: 1);
        Describe(path, "Quarterly report", "R. Elsalaymeh");
        var output = Path.Combine(_temp.Path, "smaller.pdf");

        var result = await _editor.CompressPdfAsync(path, output, new PdfCompressionOptions());

        result.Success.Should().BeTrue();
        using var compressed = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        compressed.Info.Title.Should().Be("Quarterly report");
        compressed.Info.Author.Should().Be("R. Elsalaymeh");
    }

    [Fact]
    public async Task Compression_KeepsThePages()
    {
        var path = Pdf("pages.pdf", pageCount: 3);
        var output = Path.Combine(_temp.Path, "smaller.pdf");

        var result = await _editor.CompressPdfAsync(path, output, new PdfCompressionOptions());

        result.PagesProcessed.Should().Be(3);
        PdfContent.TextOn(output, 3).Should().Contain(PageText, "compressing a file does not throw its content away");
    }

    // ---------------------------------------------------------------- redrawing annotations

    [Fact]
    public void RedrawScale_IsOneAtTheZoomTheAnnotationWasDrawnAt()
    {
        var scale = PdfPageGeometry.CanvasCoordinateScale(1.5);

        PdfPageGeometry.RedrawScale(scale, 1.5).Should().Be(1.0);
    }

    [Theory]
    [InlineData(1.0, 2.0, 2.0)]
    [InlineData(2.0, 1.0, 0.5)]
    [InlineData(1.0, 0.5, 0.5)]
    public void RedrawScale_GrowsAndShrinksWithTheZoom(double drawnAt, double shownAt, double expected)
    {
        var scale = PdfPageGeometry.CanvasCoordinateScale(drawnAt);

        PdfPageGeometry.RedrawScale(scale, shownAt).Should().BeApproximately(expected, 1e-9);
    }

    [Fact]
    public void RedrawScale_KeepsAnAnnotationOnTheSamePointOfThePage()
    {
        // Drawn 200 canvas pixels across at 100%, then the page is redrawn at 175%.
        const double drawnAt = 1.0;
        const double shownAt = 1.75;
        const double canvasX = 200;

        var drawnScale = PdfPageGeometry.CanvasCoordinateScale(drawnAt);
        var redrawn = canvasX * PdfPageGeometry.RedrawScale(drawnScale, shownAt);

        var pointsWhenDrawn = canvasX * drawnScale;
        var pointsWhenRedrawn = redrawn * PdfPageGeometry.CanvasCoordinateScale(shownAt);
        pointsWhenRedrawn.Should().BeApproximately(pointsWhenDrawn, 1e-9, "it is the same spot on the page");
    }

    [Fact]
    public void RedrawScale_RefusesAScaleThatCannotBeOne()
    {
        var refuse = (double scale) => () => PdfPageGeometry.RedrawScale(scale, 1.0);

        refuse(0).Should().Throw<ArgumentOutOfRangeException>();
        refuse(-1).Should().Throw<ArgumentOutOfRangeException>();
        refuse(double.PositiveInfinity).Should().Throw<ArgumentOutOfRangeException>();
    }

    // ---------------------------------------------------------------- helpers

    private string Pdf(string name, int pageCount)
    {
        var path = Path.Combine(_temp.Path, name);
        PdfTestFile.WritePagesWithSecretText(path, pageCount, PageText);
        return path;
    }

    private async Task<PdfEditorDocument> OpenAsync(int pageCount = 1)
    {
        var document = await _editor.OpenPdfAsync(Pdf("input.pdf", pageCount));
        document.Should().NotBeNull();
        return document!;
    }

    private static TextAnnotation Text(PdfEditorDocument document, string text, int pagePosition = 1) => new()
    {
        PageId = document.Pages[pagePosition - 1].Id,
        Text = text,
        X = 72,
        Y = 72,
        Width = 200,
        Height = 20,
    };

    private static void Describe(string path, string title, string author)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Modify);
        document.Info.Title = title;
        document.Info.Author = author;
        document.Save(path);
    }

    private static string WordText(string path)
    {
        using var word = WordprocessingDocument.Open(path, false);
        return word.MainDocumentPart?.Document.Body?.InnerText ?? "";
    }

    private static int PageBreaks(string path)
    {
        using var word = WordprocessingDocument.Open(path, false);
        return word.MainDocumentPart?.Document.Body
            ?.Descendants<DocumentFormat.OpenXml.Wordprocessing.Break>()
            .Count(b => b.Type?.Value == DocumentFormat.OpenXml.Wordprocessing.BreakValues.Page) ?? 0;
    }
}
