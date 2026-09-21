using FluentAssertions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// Saving is not supposed to be a way of losing things. <c>SavePdfAsync</c> builds a new document and copies
/// the pages across, so anything that lives on the document rather than on a page — its title and author,
/// its bookmarks — starts out absent and stays absent unless it is carried over deliberately.
/// <para>
/// <c>CopyDescription</c> exists for exactly this and was wired into compression only, so every ordinary
/// save stripped the file's description. Nothing in the UI said so; the file simply came back anonymous.
/// </para>
/// </summary>
public class PdfSaveFidelityTests : IDisposable
{
    private readonly TempDirectory _temp = new("pdf-fidelity");
    private readonly PdfEditor _editor = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task SavingADocument_KeepsItsTitleAndAuthor()
    {
        var source = Described("described.pdf");

        var saved = await SaveAsync(source, "saved.pdf");

        using var written = PdfReader.Open(saved, PdfDocumentOpenMode.Import);
        written.Info.Title.Should().Be("Quarterly Report");
        written.Info.Author.Should().Be("A. Tester");
        written.Info.Subject.Should().Be("Revenue");
        written.Info.Keywords.Should().Be("revenue, quarter");
    }

    [Fact]
    public async Task SavingADocument_KeepsItsBookmarks()
    {
        var source = Described("bookmarked.pdf");

        var saved = await SaveAsync(source, "saved-bookmarks.pdf");

        using var written = PdfReader.Open(saved, PdfDocumentOpenMode.Modify);
        written.Outlines.Count.Should().Be(2, "a document's bookmarks are how a reader navigates it");
        written.Outlines[0].Title.Should().Be("Summary");
        written.Outlines[1].Title.Should().Be("Detail");
    }

    [Fact]
    public async Task ReorderingPagesThenSavingTwice_KeepsTheOrderTheUserChose()
    {
        // Three pages, each marked in its own place, so a rendering says which page of the source it is.
        var path = Path.Combine(_temp.Path, "reorder.pdf");
        PdfTestFile.WriteNumberedPages(path, pageCount: 3);

        var document = await _editor.OpenPdfAsync(path);
        document.Should().NotBeNull();

        // Move the last page to the front: 3, 1, 2.
        var last = document!.Pages[2];
        document.Pages.RemoveAt(2);
        document.Pages.Insert(0, last);

        var first = await SaveAsync(document, "reordered-once.pdf");
        await AssertOrderAsync(first, [3, 1, 2]);

        // Saving again, with no further edits, must produce the same file. The page numbers the document
        // holds point into the *source* file; after a save-over they point into the saved one, and nothing
        // renumbered them.
        var second = await SaveAsync(document, "reordered-twice.pdf");
        await AssertOrderAsync(second, [3, 1, 2]);
    }

    [Fact]
    public async Task SavingOverTheSourceThenSavingAgain_KeepsTheOrderTheUserChose()
    {
        var path = Path.Combine(_temp.Path, "in-place.pdf");
        PdfTestFile.WriteNumberedPages(path, pageCount: 3);

        var document = await _editor.OpenPdfAsync(path);
        document.Should().NotBeNull();

        var last = document!.Pages[2];
        document.Pages.RemoveAt(2);
        document.Pages.Insert(0, last);

        // Save over the file it was opened from, which is what Ctrl+S does.
        (await _editor.SavePdfAsync(document, path)).Success.Should().BeTrue();
        await AssertOrderAsync(path, [3, 1, 2]);

        (await _editor.SavePdfAsync(document, path)).Success.Should().BeTrue();
        await AssertOrderAsync(path, [3, 1, 2], "the second save must not shuffle what the first one wrote");
    }

    [Fact]
    public async Task SavingADocumentWithFormFields_SaysTheyWereNotKept()
    {
        // Form fields are not carried across. Every page is re-imported into a new document, and the
        // catalog-level /AcroForm that lists the fields does not come with them. Rebuilding it so that
        // hierarchical fields still work is more than this editor can promise - so it says so, rather than
        // returning a file whose form looks present and does not work.
        var source = WithFormFields("form.pdf");

        var document = await _editor.OpenPdfAsync(source);
        document.Should().NotBeNull();

        var result = await _editor.SavePdfAsync(document!, Path.Combine(_temp.Path, "saved-form.pdf"));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Warnings.Should().ContainSingle()
            .Which.Should().Contain("form", Exactly.Once(), "the user has to be told what the save dropped");
    }

    [Fact]
    public async Task SavingADocumentWithNoFormFields_WarnsAboutNothing()
    {
        var source = Described("plain.pdf");

        var document = await _editor.OpenPdfAsync(source);
        var result = await _editor.SavePdfAsync(document!, Path.Combine(_temp.Path, "saved-plain.pdf"));

        result.Success.Should().BeTrue(result.ErrorMessage);
        result.Warnings.Should().BeEmpty("a warning on every save is a warning nobody reads");
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A one-page PDF carrying an /AcroForm, which is what a fillable form is.</summary>
    private string WithFormFields(string name)
    {
        var path = Path.Combine(_temp.Path, name);

        using (var document = new PdfDocument())
        {
            document.AddPage();

            var form = new PdfDictionary(document);
            form.Elements["/Fields"] = new PdfArray(document);
            document.Internals.Catalog.Elements["/AcroForm"] = form;

            document.Save(path);
        }

        return path;
    }

    /// <summary>A one-page PDF carrying a description and two bookmarks.</summary>
    private string Described(string name)
    {
        var path = Path.Combine(_temp.Path, name);

        using (var document = new PdfDocument())
        {
            document.Info.Title = "Quarterly Report";
            document.Info.Author = "A. Tester";
            document.Info.Subject = "Revenue";
            document.Info.Keywords = "revenue, quarter";

            var page = document.AddPage();
            document.Outlines.Add("Summary", page, true);
            document.Outlines.Add("Detail", page, true);

            document.Save(path);
        }

        return path;
    }

    private async Task<string> SaveAsync(string sourcePath, string name)
    {
        var document = await _editor.OpenPdfAsync(sourcePath);
        document.Should().NotBeNull();
        return await SaveAsync(document!, name);
    }

    private async Task<string> SaveAsync(PdfEditorDocument document, string name)
    {
        var path = Path.Combine(_temp.Path, name);
        var result = await _editor.SavePdfAsync(document, path);
        result.Success.Should().BeTrue(result.ErrorMessage);
        return path;
    }

    /// <summary>Checks which source page each page of <paramref name="path"/> came from, by its mark.</summary>
    private static async Task AssertOrderAsync(string path, int[] expected, string because = "")
    {
        for (var position = 0; position < expected.Length; position++)
        {
            var rendered = await RenderedPage.RenderAsync(path, position + 1);
            PdfTestFile.MarkedPageNumber(rendered).Should().Be(expected[position],
                $"page {position + 1} of the saved file should be source page {expected[position]}. {because}");
        }
    }
}
