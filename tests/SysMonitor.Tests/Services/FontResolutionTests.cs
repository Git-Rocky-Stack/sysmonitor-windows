using FluentAssertions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using SysMonitor.Core.Services.Utilities;
using SysMonitor.Tests.TestSupport;
using Xunit;

namespace SysMonitor.Tests.Services;

/// <summary>
/// PDFsharp 6 ships almost no fonts. Measured with 6.1.1, "Arial" and "Courier New" resolve and "Consolas",
/// "Segoe UI" and "Helvetica" each throw "No appropriate font found for family name". Every PDF feature that
/// drew with one of those - converting a text, CSV or XML file, sticky notes, stamps - failed on every
/// machine, whatever fonts it had installed. WindowsFontResolver goes and finds them.
/// </summary>
public class FontResolutionTests : IDisposable
{
    private readonly TempDirectory _temp = new("pdf-fonts");
    private readonly WindowsFontResolver _resolver = new();

    public void Dispose() => _temp.Dispose();

    [Theory]
    [InlineData("Consolas")]
    [InlineData("Segoe UI")]
    [InlineData("Arial")]
    [InlineData("Times New Roman")]
    public void TheFontsWindowsShipsAreFound(string family)
    {
        var face = _resolver.ResolveTypeface(family, isBold: false, isItalic: false);

        face.Should().NotBeNull();
        _resolver.GetFont(face!.FaceName).Should().NotBeNullOrEmpty("the file behind it is readable");
    }

    [Fact]
    public void AFamilyThisMachineDoesNotHaveIsDrawnInOneItDoes()
    {
        _resolver.IsInstalled("NoSuchFontAnywhere").Should().BeFalse();

        var face = _resolver.ResolveTypeface("NoSuchFontAnywhere", isBold: false, isItalic: false);

        face.Should().NotBeNull("a missing font is not worth losing the document over");
        face!.FaceName.Should().StartWith(WindowsFontResolver.FallbackFamily);
        _resolver.GetFont(face.FaceName).Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void BoldAndItalicPickTheirOwnFace()
    {
        var regular = _resolver.ResolveTypeface("Arial", isBold: false, isItalic: false)!.FaceName;
        var bold = _resolver.ResolveTypeface("Arial", isBold: true, isItalic: false)!.FaceName;
        var italic = _resolver.ResolveTypeface("Arial", isBold: false, isItalic: true)!.FaceName;
        var boldItalic = _resolver.ResolveTypeface("Arial", isBold: true, isItalic: true)!.FaceName;

        regular.Should().Be("Arial");
        bold.Should().Be("Arial Bold");
        italic.Should().Be("Arial Italic");
        boldItalic.Should().Be("Arial Bold Italic");

        new[] { regular, bold, italic, boldItalic }
            .Select(_resolver.GetFont)
            .Should().OnlyContain(bytes => bytes != null && bytes.Length > 0);
    }

    [Fact]
    public void GetFont_ReturnsNothingForAFaceThatWasNeverResolved()
    {
        _resolver.GetFont("Not A Real Face Name").Should().BeNull();
    }

    [Fact]
    public void APdfCanBeWrittenInAFontPdfSharpDoesNotCarry()
    {
        // The end of it: this threw before, so converting a .txt or .csv to PDF could not work at all.
        WindowsFontResolver.Install();
        var path = Path.Combine(_temp.Path, "consolas.pdf");

        using (var document = new PdfDocument())
        {
            var page = document.AddPage();
            using var gfx = XGraphics.FromPdfPage(page);
            gfx.DrawString("Sales grew 14 percent", new XFont("Consolas", 12), XBrushes.Black,
                new XRect(72, 72, 400, 30), XStringFormats.TopLeft);
            document.Save(path);
        }

        PdfContent.TextOn(path, 1).Should().Contain("Sales grew 14 percent");
        PdfContent.FontCountOn(path, 1).Should().BeGreaterThan(0, "the page carries the font it was drawn with");
    }
}
