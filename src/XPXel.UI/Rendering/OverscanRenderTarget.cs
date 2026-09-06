using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using XPXel.Core.Animation;
using XPXel.Core.Documents;
using XPXel.Core.Imaging;
using XPXel.Core.Primitives;
using XPXel.Core.Rendering;

namespace XPXel.UI.Rendering;

/// <summary>
/// Flattens a region of a document that is <em>not</em> the canvas, so the crop tool can show
/// the artwork currently hanging over the edge.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists at all.</b> Moving a layer keeps what leaves the canvas rather than cutting
/// it, so a document has artwork outside its own bounds. Nothing else in XP-xel may look at it —
/// what the document <em>is</em> stops at its bounds, and every export, thumbnail and save agrees
/// on that — but dragging a crop frame outwards into a blank void would be a guess every time.
/// </para>
/// <para>
/// <b>Separate from <see cref="DocumentRenderTarget"/>, not a mode of it.</b> That class exists to
/// make a stroke cheap: it tracks dirty rectangles, keeps a bitmap the exact size of the canvas,
/// and is asked for its bitmap many times a second. This is asked once when a crop frame changes
/// size, redraws everything each time, and is thrown away the moment the tool is put down. Sharing
/// one class would mean the fast path carrying the slow path's bookkeeping.
/// </para>
/// <para>
/// <b>It refuses to render an absurd region.</b> A frame dragged far enough would ask for a buffer
/// of hundreds of megabytes; past a limit the preview simply does not appear, which is a great deal
/// better than the application stopping while it allocates.
/// </para>
/// </remarks>
public sealed class OverscanRenderTarget : IDisposable
{
    /// <summary>
    /// The largest region that will be composited, in pixels. Roughly a 4K square: past this the
    /// preview is not worth the pause it would cost.
    /// </summary>
    private const long MaximumArea = 16L * 1024 * 1024;

    private readonly LayerCompositor _compositor = new();

    private WriteableBitmap? _bitmap;
    private PixelBuffer? _buffer;
    private ImageRect _region;
    private bool _isStale = true;

    /// <summary>The region the current bitmap covers, in document coordinates.</summary>
    public ImageRect Region => _region;

    /// <summary>Marks the flattened image as needing to be built again.</summary>
    /// <remarks>
    /// Called for any pixel or structure change. There is no dirty-rectangle tracking here on
    /// purpose: the preview is rebuilt when a crop frame changes size, not while an artist draws.
    /// </remarks>
    public void Invalidate() => _isStale = true;

    /// <summary>
    /// Returns the flattened artwork for a region, rebuilding it only when it has to.
    /// </summary>
    /// <param name="document">The document to composite.</param>
    /// <param name="frame">The frame to composite.</param>
    /// <param name="region">The region wanted, in document coordinates.</param>
    /// <returns>The bitmap, or <see langword="null"/> if the region is empty or too large.</returns>
    public WriteableBitmap? GetBitmap(PixelDocument document, Frame frame, ImageRect region)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(frame);

        if (region.IsEmpty || (long)region.Width * region.Height > MaximumArea)
        {
            return null;
        }

        if (_bitmap is not null && !_isStale && region == _region)
        {
            return _bitmap;
        }

        if (_buffer is null || _region != region)
        {
            _buffer = new PixelBuffer(region.Size);

            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(
                new PixelSize(region.Width, region.Height),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Premul);

            _region = region;
        }

        _compositor.Compose(document, frame, _buffer, region, new ImagePoint(region.X, region.Y));
        Upload(_buffer, _bitmap!);
        _isStale = false;

        return _bitmap;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _buffer = null;
    }

    /// <summary>
    /// Converts the straight-alpha buffer into the premultiplied BGRA the renderer uploads.
    /// </summary>
    private static void Upload(PixelBuffer source, WriteableBitmap destination)
    {
        int width = source.Width;
        byte[] row = new byte[width * PixelBuffer.BytesPerPixel];

        using ILockedFramebuffer framebuffer = destination.Lock();

        for (int y = 0; y < source.Height; y++)
        {
            ReadOnlySpan<byte> pixels = source.GetReadOnlyRowSpan(y);

            for (int x = 0; x < width; x++)
            {
                int i = x * PixelBuffer.BytesPerPixel;
                byte alpha = pixels[i + 3];

                // Premultiplied and swizzled to BGRA in one pass, as the main render target
                // does; the two are the only places in XP-xel that premultiply.
                row[i] = (byte)(pixels[i + 2] * alpha / 255);
                row[i + 1] = (byte)(pixels[i + 1] * alpha / 255);
                row[i + 2] = (byte)(pixels[i] * alpha / 255);
                row[i + 3] = alpha;
            }

            Marshal.Copy(row, 0, framebuffer.Address + (y * framebuffer.RowBytes), row.Length);
        }
    }
}
