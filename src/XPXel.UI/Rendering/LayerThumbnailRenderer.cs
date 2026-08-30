using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System.Runtime.InteropServices;
using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.UI.Rendering;

/// <summary>
/// Renders the small preview image shown beside each row of the layers panel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this does not use <see cref="XPXel.Core.Rendering.LayerCompositor"/>.</b> The
/// compositor flattens an entire document for a frame. A thumbnail needs the opposite: one
/// layer's own contribution, in isolation, so that hiding a layer above it does not change
/// the preview and so that a hidden layer still shows what it contains. Sharing the
/// compositor would mean giving it a "just this subtree, ignore visibility" mode that
/// nothing else wants.
/// </para>
/// <para>
/// <b>Box averaging, not nearest sampling.</b> Nearest sampling is right for the canvas,
/// where the artist must see exact pixels, but wrong here: dropping three quarters of the
/// rows to fit a 32 pixel preview makes a one pixel outline disappear entirely, and an
/// outline is often the only thing that identifies a layer. Averaging keeps a recognisable
/// silhouette. The cost is proportional to the canvas, not the thumbnail, which is why
/// callers must throttle rather than rebuild on every pointer move — see
/// <c>LayerItemViewModel</c>.
/// </para>
/// <para>
/// The output is premultiplied BGRA because that is what the renderer uploads. This is one
/// of the two places in XP-xel that premultiplies; the other is
/// <see cref="DocumentRenderTarget"/>. Nothing in Core or Formats may.
/// </para>
/// </remarks>
internal static class LayerThumbnailRenderer
{
    /// <summary>The longest edge of a generated thumbnail, in pixels.</summary>
    public const int MaximumSize = 32;

    /// <summary>
    /// Renders one layer, and its descendants if it is a group, into a new bitmap.
    /// </summary>
    /// <param name="document">The document that owns the layer and the cels.</param>
    /// <param name="layer">The layer to preview.</param>
    /// <param name="frame">The frame whose cels should be shown.</param>
    /// <returns>
    /// A bitmap no larger than <see cref="MaximumSize"/> on its longest edge, or
    /// <see langword="null"/> if the document has no area to draw.
    /// </returns>
    public static WriteableBitmap? Render(PixelDocument document, LayerNode layer, Frame frame)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(frame);

        ImageSize canvas = document.Size;
        if (canvas.Width <= 0 || canvas.Height <= 0)
        {
            return null;
        }

        // Preserve the document's aspect ratio so a wide sprite does not preview as a
        // square, which would misrepresent what the layer actually contains.
        (int width, int height) = FitWithin(canvas, MaximumSize);

        // Flatten the layer into a straight-alpha buffer first, at full canvas resolution.
        // Compositing and downscaling in one pass would blend already-averaged samples,
        // which produces visibly wrong edges where a transparent pixel meets an opaque one.
        PixelBuffer flattened = new(canvas);
        Flatten(layer, document, frame, flattened);

        WriteableBitmap bitmap = new(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Premul);

        byte[] output = new byte[width * height * 4];
        Downscale(flattened, width, height, output);

        using (ILockedFramebuffer buffer = bitmap.Lock())
        {
            // Marshal.Copy rather than an unsafe block: XP-xel compiles without
            // AllowUnsafeBlocks everywhere except the browser head's JS interop.
            Marshal.Copy(output, 0, buffer.Address, output.Length);
        }

        return bitmap;
    }

    /// <summary>Scales a size down to fit a square bound, never below one pixel.</summary>
    private static (int Width, int Height) FitWithin(ImageSize size, int bound)
    {
        if (size.Width <= bound && size.Height <= bound)
        {
            return (size.Width, size.Height);
        }

        double scale = Math.Min((double)bound / size.Width, (double)bound / size.Height);
        return (Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
    }

    /// <summary>
    /// Draws a layer subtree into a buffer, ignoring the layer's own visibility and opacity.
    /// </summary>
    /// <remarks>
    /// Visibility is deliberately ignored for the layer being previewed: the thumbnail's job
    /// is to answer "what is on this layer", and a hidden layer showing an empty preview
    /// would make it impossible to find again. Nested children inside a group <i>do</i>
    /// honour their own visibility, because there the preview is showing the group's result.
    /// </remarks>
    private static void Flatten(LayerNode layer, PixelDocument document, Frame frame, PixelBuffer target)
    {
        switch (layer)
        {
            case RasterLayer raster:
            {
                if (document.Cels.Find(raster.Id, frame.Id) is { } cel)
                {
                    BlendCel(cel, target);
                }

                break;
            }

            case GroupLayer group:
            {
                foreach (LayerNode child in group.Children)
                {
                    if (child.IsVisible && child.Opacity > 0.0)
                    {
                        Flatten(child, document, frame, target);
                    }
                }

                break;
            }

            default:
                // An unknown layer kind previews as empty rather than throwing, matching how
                // the compositor treats one.
                break;
        }
    }

    /// <summary>Source-over composites one cel into the buffer at the cel's offset.</summary>
    private static void BlendCel(Cel cel, PixelBuffer target)
    {
        ImageRect bounds = cel.Bounds.Intersect(new ImageRect(ImagePoint.Origin, target.Size));

        for (int y = bounds.Top; y < bounds.Bottom; y++)
        {
            for (int x = bounds.Left; x < bounds.Right; x++)
            {
                PixelColor source = cel.Pixels.GetPixel(x - cel.Offset.X, y - cel.Offset.Y);
                if (source.A == 0)
                {
                    continue;
                }

                target.SetPixel(x, y, ColorBlending.SourceOver(source, target.GetPixel(x, y)));
            }
        }
    }

    /// <summary>
    /// Box-averages a straight-alpha buffer into a premultiplied BGRA byte array.
    /// </summary>
    /// <remarks>
    /// Colour is averaged <i>weighted by alpha</i>. Averaging the raw channels instead would
    /// let the arbitrary colour stored in fully transparent pixels bleed into the result,
    /// which shows up as a dark halo around sprites whose transparent areas happen to be
    /// stored as transparent black.
    /// </remarks>
    private static void Downscale(PixelBuffer source, int width, int height, byte[] destination)
    {
        int sourceWidth = source.Size.Width;
        int sourceHeight = source.Size.Height;

        for (int y = 0; y < height; y++)
        {
            int y0 = y * sourceHeight / height;
            int y1 = Math.Max(y0 + 1, (y + 1) * sourceHeight / height);

            for (int x = 0; x < width; x++)
            {
                int x0 = x * sourceWidth / width;
                int x1 = Math.Max(x0 + 1, (x + 1) * sourceWidth / width);

                long weightedRed = 0;
                long weightedGreen = 0;
                long weightedBlue = 0;
                long totalAlpha = 0;
                int samples = 0;

                for (int sy = y0; sy < y1; sy++)
                {
                    for (int sx = x0; sx < x1; sx++)
                    {
                        PixelColor pixel = source.GetPixel(sx, sy);
                        weightedRed += pixel.R * pixel.A;
                        weightedGreen += pixel.G * pixel.A;
                        weightedBlue += pixel.B * pixel.A;
                        totalAlpha += pixel.A;
                        samples++;
                    }
                }

                int index = ((y * width) + x) * 4;
                if (totalAlpha == 0 || samples == 0)
                {
                    destination[index] = 0;
                    destination[index + 1] = 0;
                    destination[index + 2] = 0;
                    destination[index + 3] = 0;
                    continue;
                }

                // Averaged coverage, then premultiplied: the stored value is colour × alpha.
                byte alpha = (byte)(totalAlpha / samples);
                destination[index] = (byte)(weightedBlue / totalAlpha * alpha / 255);
                destination[index + 1] = (byte)(weightedGreen / totalAlpha * alpha / 255);
                destination[index + 2] = (byte)(weightedRed / totalAlpha * alpha / 255);
                destination[index + 3] = alpha;
            }
        }
    }
}
