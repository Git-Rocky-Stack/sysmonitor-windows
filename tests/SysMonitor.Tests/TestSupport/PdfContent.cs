using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace SysMonitor.Tests.TestSupport;

/// <summary>Reads what a saved page actually contains, which is what a reader or an extraction tool sees.</summary>
internal static class PdfContent
{
    private static readonly HashSet<string> TextOperators = ["Tj", "TJ", "'", "\""];

    /// <summary>Every piece of text the page's content stream shows, in the order it is drawn.</summary>
    public static List<string> TextOn(string pdfPath, int pageNumber)
    {
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var shown = new List<string>();
        Collect(ContentReader.ReadContent(document.Pages[pageNumber - 1]), shown);
        return shown;
    }

    /// <summary>The fonts the page keeps in its resources; a page with no text has none.</summary>
    public static int FontCountOn(string pdfPath, int pageNumber)
    {
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var resources = document.Pages[pageNumber - 1].Elements.GetDictionary("/Resources");
        var fonts = resources?.Elements.GetDictionary("/Font");
        return fonts?.Elements.Count ?? 0;
    }

    /// <summary>The images the page draws; a page saved as a picture of itself has one.</summary>
    public static int ImageCountOn(string pdfPath, int pageNumber)
    {
        using var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
        var resources = document.Pages[pageNumber - 1].Elements.GetDictionary("/Resources");
        var xObjects = resources?.Elements.GetDictionary("/XObject");
        return xObjects?.Elements.Count ?? 0;
    }

    private static void Collect(CObject content, List<string> shown)
    {
        switch (content)
        {
            case CSequence sequence:
                foreach (var item in sequence)
                    Collect(item, shown);
                break;

            case COperator op when TextOperators.Contains(op.OpCode.Name):
                foreach (var operand in op.Operands)
                    Collect(operand, shown);
                break;

            // CArray is a CSequence, so the case above already walks the strings inside a TJ array.
            case CString text:
                shown.Add(text.Value);
                break;
        }
    }
}
