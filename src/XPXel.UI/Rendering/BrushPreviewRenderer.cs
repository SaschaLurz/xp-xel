using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using XPXel.Core.Drawing;
using XPXel.Core.Primitives;

namespace XPXel.UI.Rendering;

/// <summary>
/// Draws the brush tip as it will actually be stamped, for the preview in the brush panel.
/// </summary>
/// <remarks>
/// <para>
/// <b>The preview calls the real stamping code.</b> It is not an approximation drawn with an
/// ellipse: it runs <see cref="BrushStamp.StampSoft"/> with the artist's exact size, shape and
/// softness, so what the panel shows is precisely the mark the brush will leave. A preview
/// drawn any other way would eventually disagree with the tool, and the artist would learn to
/// stop trusting it.
/// </para>
/// <para>
/// Small brushes are shown magnified so that the difference between size 1 and size 3 is
/// visible at all; large ones are shown at 1:1 and cropped by the preview box, which is
/// honest about the fact that they no longer fit.
/// </para>
/// <para>
/// The output is premultiplied BGRA because that is what the renderer uploads.
/// </para>
/// </remarks>
internal static class BrushPreviewRenderer
{
    /// <summary>The side of the preview bitmap, in image pixels before magnification.</summary>
    private const int Box = 40;

    /// <summary>
    /// Renders the brush tip.
    /// </summary>
    /// <param name="size">The brush width in pixels.</param>
    /// <param name="shape">The brush outline.</param>
    /// <param name="softness">The edge falloff, 0 to 100.</param>
    /// <param name="color">The colour to draw the tip in.</param>
    /// <returns>A bitmap of the tip on a transparent ground.</returns>
    public static WriteableBitmap Render(int size, BrushShape shape, int softness, PixelColor color)
    {
        // An odd box keeps the brush's centre on a pixel, so an odd-sized brush previews
        // symmetrically rather than leaning one pixel to a side.
        const int side = Box | 1;

        byte[] pixels = new byte[side * side * 4];
        ImagePoint center = new(side / 2, side / 2);

        BrushStamp.StampSoft(
            center,
            Math.Max(1, size),
            shape,
            Math.Clamp(softness, 0, 100) / 100.0,
            (x, y, coverage) =>
            {
                if (x < 0 || y < 0 || x >= side || y >= side)
                {
                    return;
                }

                byte alpha = (byte)Math.Round(color.A * coverage);
                int i = ((y * side) + x) * 4;

                // Premultiplied, so each channel is scaled by the alpha it is drawn at.
                pixels[i] = (byte)(color.B * alpha / 255);
                pixels[i + 1] = (byte)(color.G * alpha / 255);
                pixels[i + 2] = (byte)(color.R * alpha / 255);
                pixels[i + 3] = alpha;
            });

        WriteableBitmap bitmap = new(
            new PixelSize(side, side),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        using (ILockedFramebuffer buffer = bitmap.Lock())
        {
            Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        return bitmap;
    }
}
