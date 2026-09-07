using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using XPXel.Core.Editing;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.UI.Rendering;

/// <summary>
/// Converts between a <see cref="PixelClip"/> and an Avalonia bitmap, which is the currency
/// the system clipboard deals in.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a conversion is needed at all.</b> Every other program on the machine understands a
/// bitmap and none of them understand a clip, so copying has to put one on the clipboard beside
/// the clip and pasting has to accept one. This is the boundary where XP-xel's exact,
/// straight-alpha pixels meet the platform's premultiplied ones.
/// </para>
/// <para>
/// <b>Coverage is baked into the alpha on the way out.</b> A clip's shape lives in a mask the
/// clipboard has no idea about, so a half-covered pixel becomes a half-transparent one and an
/// uncovered pixel becomes a fully transparent one. That is what the shape looks like to
/// anything else, and it is the only lossless-in-appearance answer available.
/// </para>
/// </remarks>
internal static class ClipBitmap
{
    /// <summary>The standard screen resolution; a clip has no opinion about physical size.</summary>
    private static readonly Vector Dpi = new(96, 96);

    /// <summary>Renders a clip as a bitmap other programs can paste.</summary>
    /// <param name="clip">The clip to render.</param>
    /// <returns>A bitmap of the clip, premultiplied the way the platform expects.</returns>
    public static WriteableBitmap ToBitmap(PixelClip clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        WriteableBitmap bitmap = new(
            new PixelSize(clip.Size.Width, clip.Size.Height),
            Dpi,
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        ReadOnlySpan<PixelColor> pixels = clip.Pixels;
        ReadOnlySpan<byte> coverage = clip.Coverage;

        byte[] row = new byte[clip.Size.Width * PixelBuffer.BytesPerPixel];

        using ILockedFramebuffer framebuffer = bitmap.Lock();

        for (int y = 0; y < clip.Size.Height; y++)
        {
            for (int x = 0; x < clip.Size.Width; x++)
            {
                int index = (y * clip.Size.Width) + x;
                PixelColor color = pixels[index];

                // The mask multiplies the alpha, then the alpha multiplies the colour: what is
                // outside the marked shape ends up transparent, and everything is premultiplied
                // as the platform requires.
                byte alpha = (byte)(color.A * coverage[index] / 255);

                int i = x * PixelBuffer.BytesPerPixel;
                row[i] = (byte)(color.B * alpha / 255);
                row[i + 1] = (byte)(color.G * alpha / 255);
                row[i + 2] = (byte)(color.R * alpha / 255);
                row[i + 3] = alpha;
            }

            Marshal.Copy(row, 0, framebuffer.Address + (y * framebuffer.RowBytes), row.Length);
        }

        return bitmap;
    }

    /// <summary>Reads a bitmap from elsewhere into a clip.</summary>
    /// <param name="bitmap">The bitmap the clipboard handed over.</param>
    /// <returns>
    /// The clip, or <see langword="null"/> if the bitmap has no pixels or is implausibly large.
    /// </returns>
    /// <remarks>
    /// The bitmap is copied through a buffer of XP-xel's own choosing rather than read in place,
    /// because a bitmap from another program can be in any format and any alpha convention.
    /// Asking Avalonia to copy it into a known one is both shorter and more correct than
    /// enumerating the possibilities here.
    /// </remarks>
    public static PixelClip? FromBitmap(Bitmap? bitmap)
    {
        if (bitmap is not { PixelSize: { Width: > 0, Height: > 0 } size })
        {
            return null;
        }

        // Pasting an entire desktop screenshot into a sprite editor is a mistake rather than an
        // intention, but it must not be a crash either; the ceiling is well past any sprite.
        if ((long)size.Width * size.Height > MaximumArea)
        {
            return null;
        }

        WriteableBitmap converted = new(size, Dpi, PixelFormat.Bgra8888, AlphaFormat.Unpremul);

        using (ILockedFramebuffer framebuffer = converted.Lock())
        {
            bitmap.CopyPixels(framebuffer);
        }

        PixelColor[] pixels = new PixelColor[size.Width * size.Height];
        byte[] row = new byte[size.Width * PixelBuffer.BytesPerPixel];

        using (ILockedFramebuffer framebuffer = converted.Lock())
        {
            for (int y = 0; y < size.Height; y++)
            {
                Marshal.Copy(framebuffer.Address + (y * framebuffer.RowBytes), row, 0, row.Length);

                for (int x = 0; x < size.Width; x++)
                {
                    int i = x * PixelBuffer.BytesPerPixel;

                    // Back from BGRA to XP-xel's RGBA. The alpha is already straight, because
                    // the framebuffer above asked for it that way.
                    pixels[(y * size.Width) + x] = new PixelColor(row[i + 2], row[i + 1], row[i], row[i + 3]);
                }
            }
        }

        converted.Dispose();

        return PixelClip.FromImage(new ImageSize(size.Width, size.Height), pixels);
    }

    /// <summary>The largest image that will be accepted from the clipboard, in pixels.</summary>
    private const long MaximumArea = 8192L * 8192L;
}
