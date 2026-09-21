using FluentAssertions;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// An annotation belongs to a page, not to a place in the document: moving, deleting, inserting or
/// duplicating pages must not move it to another page or lose it. Each source page carries its own mark,
/// so every saved page can be traced back to the page of the file it came from.
/// </summary>
public class PdfPageIdentityTests : IDisposable
{
    private readonly TempDirectory _temp = new("pdf-pages");
    private readonly PdfEditor _editor = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task MovingAPage_TakesItsAnnotationsWithIt()
    {
        var document = await OpenAsync(pageCount: 3);
        await MarkWithInkAsync(document, pagePosition: 3);

        // [P1 P2 P3] -> [P3 P1 P2]
        (await _editor.ReorderPagesAsync(document, [2, 0, 1])).Success.Should().BeTrue();

        var saved = await SaveAsync(document);
        await AssertPagesAsync(saved, (Source: 3, HasInk: true), (1, false), (2, false));
    }

    [Fact]
    public async Task DeletingAPage_LeavesEveryOtherPageWithItsOwnAnnotations()
    {
        var document = await OpenAsync(pageCount: 3);
        await MarkWithInkAsync(document, pagePosition: 2);

        (await _editor.DeletePageAsync(document, 1)).Success.Should().BeTrue();

        var saved = await SaveAsync(document);
        await AssertPagesAsync(saved, (2, true), (3, false));
    }

    [Fact]
    public async Task DeletingAPage_TakesItsAnnotationsWithIt()
    {
        var document = await OpenAsync(pageCount: 2);
        await MarkWithInkAsync(document, pagePosition: 1);

        (await _editor.DeletePageAsync(document, 1)).Success.Should().BeTrue();
        document.Annotations.Should().BeEmpty("the page they were on is gone");

        var saved = await SaveAsync(document);
        await AssertPagesAsync(saved, (2, false));
    }

    [Fact]
    public async Task AnInsertedBlankPageIsSavedAndCanBeAnnotated()
    {
        var document = await OpenAsync(pageCount: 2);

        (await _editor.InsertBlankPageAsync(document, 1)).Success.Should().BeTrue();
        await MarkWithInkAsync(document, pagePosition: 2);

        var saved = await SaveAsync(document);
        var pages = await AssertPagesAsync(saved, (1, false), (0, true), (2, false));

        pages[1].Width.Should().Be(816, "a blank Letter page is 612 pt wide");
        pages[1].Height.Should().Be(1056);
    }

    [Fact]
    public async Task DuplicatingAPage_CopiesWhatWasRedactedOnIt()
    {
        var document = await OpenAsync(pageCount: 2);
        await _editor.AddRedactionAsync(document, 1, new RedactionAnnotation
        {
            X = 300,
            Y = 500,
            Width = 120,
            Height = 60,
            FillColor = "#000000",
            OverlayText = "",
            CoordinateScale = PdfPageGeometry.CanvasCoordinateScale(1.0),
        });

        (await _editor.DuplicatePageAsync(document, 1)).Success.Should().BeTrue();

        var saved = await SaveAsync(document);
        await AssertPagesAsync(saved, (1, true), (1, true), (2, false));
    }

    [Fact]
    public async Task RotatingAPage_TurnsThePageAtThatPosition()
    {
        var document = await OpenAsync(pageCount: 3);
        (await _editor.ReorderPagesAsync(document, [2, 0, 1])).Success.Should().BeTrue();

        (await _editor.RotatePageAsync(document, 1, 90)).Success.Should().BeTrue();

        var saved = await SaveAsync(document);
        var first = await RenderedPage.RenderAsync(saved, 1);
        var second = await RenderedPage.RenderAsync(saved, 2);

        PdfTestFile.MarkedPageNumber(first, rotation: 90).Should().Be(3, "page 3 was moved to the front");
        first.Width.Should().BeGreaterThan(first.Height, "and that page is the one now on its side");
        second.Width.Should().BeLessThan(second.Height);
    }

    [Fact]
    public async Task AWatermarkForOnePage_LandsOnThatPage()
    {
        var document = await OpenAsync(pageCount: 2);

        var result = await _editor.AddWatermarkAsync(document, new WatermarkAnnotation
        {
            Text = "HHHH",
            FontSize = 48,
            Rotation = 0,
            Position = WatermarkPosition.Center,
            Opacity = 1.0,
            Color = "#000000",
            ApplyToAllPages = false,
        }, pagePosition: 2);
        result.Success.Should().BeTrue(result.ErrorMessage);

        var saved = await SaveAsync(document);
        await AssertPagesAsync(saved, (1, false), (2, true));
    }

    [Fact]
    public async Task AnnotatingAPageThatIsNotThere_Fails()
    {
        var document = await OpenAsync(pageCount: 2);

        var result = await _editor.AddHighlightAsync(document, 5, new HighlightAnnotation { Width = 10, Height = 10 });

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("no page 5");
        document.Annotations.Should().BeEmpty();
    }

    [Fact]
    public async Task SavingRefusesADocumentThatExpectsAPageTheFileDoesNotHave()
    {
        var document = await OpenAsync(pageCount: 2);
        document.Pages[0].PageNumber = 7;

        var saved = Path.Combine(_temp.Path, "saved.pdf");
        var result = await _editor.SavePdfAsync(document, saved);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("only 2 pages");
        File.Exists(saved).Should().BeFalse("a save that cannot write a page writes nothing");
    }

    [Fact]
    public async Task SavingRefusesADocumentWithNoPages()
    {
        var document = await OpenAsync(pageCount: 2);
        document.Pages.Clear();

        var saved = Path.Combine(_temp.Path, "empty.pdf");
        var result = await _editor.SavePdfAsync(document, saved);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("at least one page");
        File.Exists(saved).Should().BeFalse();
    }

    private async Task<PdfEditorDocument> OpenAsync(int pageCount)
    {
        var source = Path.Combine(_temp.Path, $"source-{Guid.NewGuid():N}.pdf");
        PdfTestFile.WriteNumberedPages(source, pageCount);

        var document = await _editor.OpenPdfAsync(source);
        document.Should().NotBeNull();
        return document!;
    }

    /// <summary>Puts a black box on the page at a position, clear of that page's mark.</summary>
    private async Task MarkWithInkAsync(PdfEditorDocument document, int pagePosition)
    {
        var result = await _editor.AddHighlightAsync(document, pagePosition, new HighlightAnnotation
        {
            X = 400,
            Y = 600,
            Width = 120,
            Height = 60,
            Color = "#000000",
            Opacity = 1.0,
            CoordinateScale = PdfPageGeometry.CanvasCoordinateScale(1.0),
        });
        result.Success.Should().BeTrue(result.ErrorMessage);
    }

    private async Task<string> SaveAsync(PdfEditorDocument document)
    {
        var saved = Path.Combine(_temp.Path, $"saved-{Guid.NewGuid():N}.pdf");
        var result = await _editor.SavePdfAsync(document, saved);
        result.Success.Should().BeTrue(result.ErrorMessage);
        result.PagesProcessed.Should().Be(document.Pages.Count);
        return saved;
    }

    /// <summary>
    /// Checks the saved pages one by one: which page of the source file each one is (0 for a blank page),
    /// and whether it carries an annotation.
    /// </summary>
    private static async Task<List<RenderedPage>> AssertPagesAsync(string saved, params (int Source, bool HasInk)[] expected)
    {
        var pages = new List<RenderedPage>();
        for (var position = 1; position <= expected.Length; position++)
            pages.Add(await RenderedPage.RenderAsync(saved, position));

        for (var position = 1; position <= expected.Length; position++)
        {
            var page = pages[position - 1];
            var (source, hasInk) = expected[position - 1];

            PdfTestFile.MarkedPageNumber(page).Should().Be(source, $"of the page saved at position {position}");
            (page.BoundsOf(RenderedPage.IsInk) is not null).Should().Be(
                hasInk, $"about the annotation on the page saved at position {position}");
        }

        return pages;
    }
}
