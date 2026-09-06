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
/// Keeps an up-to-date <see cref="WriteableBitmap"/> of a document's flattened appearance,
/// ready for the canvas control to draw.
/// </summary>
/// <remarks>
/// <para>
/// This is the bridge between the editor's model of an image and the renderer's. It holds
/// two representations of the same picture — a straight-alpha <see cref="PixelBuffer"/> that
/// the compositor writes into, and a premultiplied BGRA bitmap the GPU can draw — and keeps
/// them in step.
/// </para>
/// <para>
/// <b>Dirty rectangles are the whole point.</b> A pencil dab changes one pixel. Without
/// tracking which region changed, every dab would recomposite every layer over the entire
/// canvas and re-upload the whole bitmap to the GPU, which is precisely how a pixel editor
/// ends up feeling laggy on a large canvas. Both the compositing pass and the upload are
/// therefore limited to the accumulated dirty rectangle.
/// </para>
/// <para>
/// <b>Why the conversion happens here and nowhere else.</b> Renderers want premultiplied
/// alpha; the document model deliberately does not use it (see <see cref="PixelColor"/>).
/// This class is the single boundary where that conversion occurs, which keeps the lossy
/// step out of anything that could be saved or undone.
/// </para>
/// <para>
/// Not thread-safe; it belongs to one canvas control on the UI thread.
/// </para>
/// </remarks>
public sealed class DocumentRenderTarget : IDisposable
{
    private readonly LayerCompositor _compositor = new();
    private readonly OnionSkinCompositor _onionCompositor = new();

    private PixelBuffer _composited;
    private WriteableBitmap _bitmap;
    private ImageSize _size;
    private ImageRect _dirtyRegion;
    private byte[] _uploadScratch;

    /// <summary>Creates a render target for a document of the given size.</summary>
    /// <param name="size">The document dimensions.</param>
    public DocumentRenderTarget(ImageSize size)
    {
        _size = size;
        _composited = new PixelBuffer(size);
        _bitmap = CreateBitmap(size);
        _uploadScratch = new byte[size.Width * PixelBuffer.BytesPerPixel];
        _dirtyRegion = size.ToRect();
    }

    /// <summary>The document size this target is currently sized for.</summary>
    public ImageSize Size => _size;

    /// <summary>Marks a region as needing to be recomposited before the next draw.</summary>
    /// <param name="region">The changed region in document coordinates.</param>
    public void Invalidate(ImageRect region) =>
        _dirtyRegion = _dirtyRegion.Union(region.Intersect(_size.ToRect()));

    /// <summary>Marks the whole canvas as needing to be recomposited.</summary>
    public void InvalidateAll() => _dirtyRegion = _size.ToRect();

    /// <summary>
    /// Recomposites whatever has changed and returns the bitmap to draw.
    /// </summary>
    /// <param name="document">The document to render.</param>
    /// <param name="frame">The frame to render.</param>
    /// <returns>The up-to-date bitmap.</returns>
    public WriteableBitmap GetBitmap(PixelDocument document, Frame frame) =>
        GetBitmap(document, frame, onionSkin: null, range: null);

    /// <summary>
    /// Returns the composited bitmap, optionally with the neighbouring frames ghosted under
    /// the current one.
    /// </summary>
    /// <param name="document">The document to composite.</param>
    /// <param name="frame">The frame being edited.</param>
    /// <param name="onionSkin">
    /// How to ghost the neighbouring frames, or <see langword="null"/> for none.
    /// </param>
    /// <param name="range">
    /// The animation the ghosts may be taken from, so onion skinning inside one motion does
    /// not reach into the next.
    /// </param>
    /// <returns>The bitmap, brought up to date within the dirty region.</returns>
    /// <remarks>
    /// The ghosts are composited into the same buffer as the artwork rather than drawn as a
    /// separate layer over it. Doing it here means they go through the identical premultiply
    /// and upload path as everything else, and it means the colour picker samples what is
    /// actually on screen — including, deliberately, nothing of the ghosts, because
    /// <see cref="SampleComposite"/> reads this buffer and an artist picking a colour wants
    /// the drawing, not a tinted memory of the frame before it.
    /// </remarks>
    public WriteableBitmap GetBitmap(
        PixelDocument document,
        Frame frame,
        OnionSkinSettings? onionSkin,
        AnimationTag? range)
    {
        ArgumentNullException.ThrowIfNull(document);

        // A document whose size no longer matches means everything cached here is stale.
        if (document.Size != _size)
        {
            Resize(document.Size);
        }

        if (!_dirtyRegion.IsEmpty)
        {
            if (onionSkin is { IsEnabled: true })
            {
                _onionCompositor.Compose(
                    document,
                    document.IndexOfFrame(frame),
                    _composited,
                    _dirtyRegion,
                    onionSkin,
                    range);
            }
            else
            {
                _compositor.Compose(document, frame, _composited, _dirtyRegion);
            }

            UploadRegion(_dirtyRegion);
            _dirtyRegion = ImageRect.Empty;
        }

        return _bitmap;
    }

    /// <summary>
    /// Reads a pixel of the flattened image. This is what the colour picker samples, and it
    /// is why the tool receives the colour the artist can actually see rather than whatever
    /// happens to be on the active layer.
    /// </summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns>The composited colour, or transparent outside the canvas.</returns>
    public PixelColor SampleComposite(int x, int y) => _composited.GetPixelOrTransparent(x, y);

    /// <inheritdoc />
    public void Dispose() => _bitmap.Dispose();

    private void Resize(ImageSize size)
    {
        _bitmap.Dispose();

        _size = size;
        _composited = new PixelBuffer(size);
        _bitmap = CreateBitmap(size);
        _uploadScratch = new byte[size.Width * PixelBuffer.BytesPerPixel];
        _dirtyRegion = size.ToRect();
    }

    private static WriteableBitmap CreateBitmap(ImageSize size) => new(
        new PixelSize(Math.Max(1, size.Width), Math.Max(1, size.Height)),
        new Vector(96, 96),
        PixelFormat.Bgra8888,
        AlphaFormat.Premul);

    /// <summary>
    /// Copies part of the composited buffer into the bitmap, converting straight-alpha RGBA
    /// into premultiplied BGRA on the way.
    /// </summary>
    private void UploadRegion(ImageRect region)
    {
        ImageRect area = region.Intersect(_size.ToRect());
        if (area.IsEmpty)
        {
            return;
        }

        using ILockedFramebuffer framebuffer = _bitmap.Lock();
        int rowBytes = area.Width * PixelBuffer.BytesPerPixel;

        for (int y = area.Top; y < area.Bottom; y++)
        {
            ReadOnlySpan<byte> sourceRow = _composited.GetReadOnlyRowSpan(y);
            Span<byte> scratch = _uploadScratch.AsSpan(0, rowBytes);

            for (int x = 0; x < area.Width; x++)
            {
                int si = (area.Left + x) * PixelBuffer.BytesPerPixel;
                PixelColor color = new(sourceRow[si], sourceRow[si + 1], sourceRow[si + 2], sourceRow[si + 3]);
                ColorBlending.WritePremultipliedBgra(color, scratch[(x * PixelBuffer.BytesPerPixel)..]);
            }

            // Marshal.Copy rather than an unsafe span keeps this project free of unsafe code;
            // the extra scratch-row copy is negligible next to the per-pixel conversion.
            nint destination = framebuffer.Address
                + (y * framebuffer.RowBytes)
                + (area.Left * PixelBuffer.BytesPerPixel);

            Marshal.Copy(_uploadScratch, 0, destination, rowBytes);
        }
    }
}
