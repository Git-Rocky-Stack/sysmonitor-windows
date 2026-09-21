using PdfSharp.Drawing;

namespace SysMonitor.Core.Services.Utilities;

/// <summary>
/// Turns raster bytes into a picture PDFsharp can draw.
///
/// <para>
/// <c>XImage.FromStream(new MemoryStream(bytes))</c> throws. PDFsharp reads the stream through
/// <c>GetBuffer()</c>, and a <see cref="MemoryStream"/> constructed from a byte array is not publicly
/// visible, so that call is refused. The stream has to be created empty and written into. Every image the
/// editor inserted took the first path, so every insert failed - and the editor painted a grey box
/// captioned "[Image]" and reported the save as a success.
/// </para>
/// <para>
/// Callers also hand in the list that holds the streams open until the document is written. PDFsharp
/// documents <c>XImage</c> as borrowing the stream for its lifetime. Version 6.1.1 in fact buffers the
/// pixels at <c>FromStream</c> - closing the stream early was measured and changed nothing - so this is a
/// guard against a future version reading lazily, not the part of the fix that makes images appear.
/// </para>
/// </summary>
public static class PdfImageSource
{
    /// <summary>
    /// Opens <paramref name="imageBytes"/> as a PDFsharp image and registers its stream in
    /// <paramref name="keepOpenUntilSaved"/>, which the caller disposes after saving the document.
    /// </summary>
    /// <exception cref="InvalidOperationException">The bytes are not an image any supported decoder reads.</exception>
    public static XImage Open(byte[] imageBytes, ICollection<MemoryStream> keepOpenUntilSaved)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);
        ArgumentNullException.ThrowIfNull(keepOpenUntilSaved);

        if (imageBytes.Length == 0)
            throw new InvalidOperationException("The image is empty - there is nothing to draw.");

        var stream = new MemoryStream();
        stream.Write(imageBytes);
        stream.Position = 0;

        XImage image;
        try
        {
            image = XImage.FromStream(stream);
        }
        catch (Exception ex)
        {
            stream.Dispose();
            throw new InvalidOperationException(
                $"The image could not be read: {ex.Message} Supported formats are PNG, JPEG, BMP and GIF.", ex);
        }

        keepOpenUntilSaved.Add(stream);
        return image;
    }
}
