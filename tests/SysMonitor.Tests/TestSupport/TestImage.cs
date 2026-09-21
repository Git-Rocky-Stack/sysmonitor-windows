using System.Drawing;
using System.Drawing.Imaging;

namespace SysMonitor.Tests.TestSupport;

/// <summary>Small raster images to insert into a PDF, so a test can look for their colour in the result.</summary>
internal static class TestImage
{
    /// <summary>Strong red: the colour an inserted test image is filled with.</summary>
    public static bool IsInsertedImage(byte r, byte g, byte b) => r > 170 && g < 80 && b < 80;

    /// <summary>A solid red PNG, as the bytes a file or the clipboard would hand the editor.</summary>
    public static byte[] SolidRedPng(int width = 64, int height = 64) => Solid(Color.FromArgb(220, 30, 30), width, height, ImageFormat.Png);

    /// <summary>The same picture as a JPEG, because the editor accepts whatever the user picked.</summary>
    public static byte[] SolidRedJpeg(int width = 64, int height = 64) => Solid(Color.FromArgb(220, 30, 30), width, height, ImageFormat.Jpeg);

    private static byte[] Solid(Color color, int width, int height, ImageFormat format)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.Clear(color);

        using var stream = new MemoryStream();
        bitmap.Save(stream, format);
        return stream.ToArray();
    }
}
