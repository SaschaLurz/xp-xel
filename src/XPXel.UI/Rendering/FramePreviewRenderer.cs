using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;

namespace XPXel.UI.Rendering;

/// <summary>
/// Flattens one frame of a document into a bitmap at full size, for the preview panel.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="LayerThumbnailRenderer"/>, which downscales one layer to a small
/// preview. This one keeps every pixel: the preview panel magnifies the result with
/// nearest-neighbour sampling, and an averaged source would show soft edges that the artwork
/// does not have.
/// </para>
/// <para>
/// The output is premultiplied BGRA because that is what the renderer uploads. This is the
/// third and last place in XP-xel that premultiplies; nothing in Core or Formats may.
/// </para>
/// </remarks>
internal static class FramePreviewRenderer
{
    /// <summary>Renders one frame at its true size.</summary>
    /// <param name="document">The document to composite.</param>
    /// <param name="frame">The frame to draw.</param>
    /// <returns>The flattened frame, or <see langword="null"/> for a document with no area.</returns>
    public static WriteableBitmap? Render(PixelDocument document, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(frame);

        ImageSize size = document.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        PixelBuffer flattened = new LayerCompositor().ComposeToNewBuffer(document, frame);

        WriteableBitmap bitmap = new(
            new PixelSize(size.Width, size.Height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        byte[] output = new byte[size.Width * size.Height * PixelBuffer.BytesPerPixel];
        ReadOnlySpan<byte> source = flattened.AsReadOnlySpan();

        for (int i = 0; i < output.Length; i += PixelBuffer.BytesPerPixel)
        {
            byte alpha = source[i + 3];

            output[i] = (byte)(source[i + 2] * alpha / 255);
            output[i + 1] = (byte)(source[i + 1] * alpha / 255);
            output[i + 2] = (byte)(source[i] * alpha / 255);
            output[i + 3] = alpha;
        }

        using (ILockedFramebuffer buffer = bitmap.Lock())
        {
            // Marshal.Copy rather than an unsafe block: XP-xel compiles without
            // AllowUnsafeBlocks everywhere except the browser head's JS interop.
            Marshal.Copy(output, 0, buffer.Address, output.Length);
        }

        return bitmap;
    }
}
