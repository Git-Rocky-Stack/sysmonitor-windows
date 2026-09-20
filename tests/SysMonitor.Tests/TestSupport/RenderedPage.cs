using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using SysMonitor.Core.Services.Utilities;

namespace SysMonitor.Tests.TestSupport;

/// <summary>
/// A PDF page rendered the way the editor renders it for its canvas: one pixel per DIP times the zoom,
/// in the orientation a viewer shows.
/// </summary>
internal sealed class RenderedPage
{
    private readonly byte[] _pixels; // BGRA, four bytes per pixel

    public int Width { get; }
    public int Height { get; }

    private RenderedPage(byte[] pixels, int width, int height)
    {
        _pixels = pixels;
        Width = width;
        Height = height;
    }

    public static async Task<RenderedPage> RenderAsync(string pdfPath, int pageNumber = 1, double zoom = 1.0)
    {
        var png = await PdfPageRasterizer.RenderPngAsync(pdfPath, pageNumber, zoom);
        using var stream = new MemoryStream(png);
        using var bitmap = new Bitmap(stream);

        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride < 0)
                throw new NotSupportedException("Bottom-up bitmaps are not handled.");

            var pixels = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++)
                Marshal.Copy(data.Scan0 + (y * data.Stride), pixels, y * bitmap.Width * 4, bitmap.Width * 4);

            return new RenderedPage(pixels, bitmap.Width, bitmap.Height);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    /// <summary>Strong blue: the colour a test page marks a spot with.</summary>
    public static bool IsMark(byte r, byte g, byte b) => b > 140 && r < 90 && g < 90;

    /// <summary>Near-black: the colour test annotations are drawn in.</summary>
    public static bool IsInk(byte r, byte g, byte b) => r < 70 && g < 70 && b < 70;

    public int Count(Func<byte, byte, byte, bool> matches)
    {
        var count = 0;
        ForEachPixel((_, _, r, g, b) => { if (matches(r, g, b)) count++; });
        return count;
    }

    /// <summary>The smallest box holding every matching pixel, or null when there are none.</summary>
    public PixelBox? BoundsOf(Func<byte, byte, byte, bool> matches)
    {
        int left = int.MaxValue, top = int.MaxValue, right = -1, bottom = -1;
        ForEachPixel((x, y, r, g, b) =>
        {
            if (!matches(r, g, b))
                return;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        });

        return right < 0 ? null : new PixelBox(left, top, right, bottom);
    }

    private void ForEachPixel(Action<int, int, byte, byte, byte> action)
    {
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var i = ((y * Width) + x) * 4;
                action(x, y, _pixels[i + 2], _pixels[i + 1], _pixels[i]);
            }
        }
    }
}

/// <summary>A box of pixels, both corners included.</summary>
internal readonly record struct PixelBox(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left + 1;
    public int Height => Bottom - Top + 1;
    public double CenterX => (Left + Right + 1) / 2.0;
    public double CenterY => (Top + Bottom + 1) / 2.0;

    public PixelBox Expand(int pixels) => new(Left - pixels, Top - pixels, Right + pixels, Bottom + pixels);

    /// <summary>Where this box ends up when a page of the given size is turned clockwise.</summary>
    public PixelBox Rotate(int degrees, int pageWidth, int pageHeight) =>
        ((((degrees % 360) + 360) % 360) switch
        {
            90 => new PixelBox(pageHeight - 1 - Bottom, Left, pageHeight - 1 - Top, Right),
            180 => new PixelBox(pageWidth - 1 - Right, pageHeight - 1 - Bottom, pageWidth - 1 - Left, pageHeight - 1 - Top),
            270 => new PixelBox(Top, pageWidth - 1 - Right, Bottom, pageWidth - 1 - Left),
            _ => this,
        });

    public override string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
}
