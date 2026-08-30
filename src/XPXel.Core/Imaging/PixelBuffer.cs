using XPXel.Core.Primitives;

namespace XPXel.Core.Imaging;

/// <summary>
/// A mutable rectangular grid of <see cref="PixelColor"/> values — the single storage type
/// for raster data anywhere in XP-xel.
/// </summary>
/// <remarks>
/// <para>
/// <b>Memory layout.</b> Pixels are stored row-major in one flat <see cref="byte"/> array,
/// four bytes per pixel in R, G, B, A order with <em>straight</em> (non-premultiplied)
/// alpha. The byte index of pixel (x, y) is <c>((y * Width) + x) * BytesPerPixel</c>. This
/// layout is identical to PNG's, so the codec can read and write rows without swizzling,
/// and it converts to the renderer's premultiplied BGRA in a single pass at upload time.
/// </para>
/// <para>
/// <b>Everything is a buffer.</b> Layer contents (cels), the composited document image, the
/// undo system's before/after snapshots and imported images are all PixelBuffers. Keeping
/// one type means an operation written once — a blit, a fill, a filter — works everywhere.
/// </para>
/// <para>
/// <b>Performance note for future work.</b> Methods here take and return
/// <see cref="Span{T}"/> rather than arrays so that callers can operate on rows or regions
/// without allocating. When adding bulk operations (filters, transforms, scaling), write
/// them against spans and process whole rows; per-pixel virtual calls are the usual cause
/// of a sluggish editor.
/// </para>
/// </remarks>
public sealed class PixelBuffer
{
    /// <summary>The number of bytes each pixel occupies: one per R, G, B and A channel.</summary>
    public const int BytesPerPixel = 4;

    private readonly byte[] _pixels;

    /// <summary>
    /// Creates a buffer of the given size, filled with fully transparent black.
    /// </summary>
    /// <param name="size">The dimensions of the buffer.</param>
    public PixelBuffer(ImageSize size)
    {
        Size = size;
        _pixels = new byte[size.PixelCount * BytesPerPixel];
    }

    /// <summary>Creates a buffer of the given size, filled with fully transparent black.</summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    public PixelBuffer(int width, int height)
        : this(new ImageSize(width, height))
    {
    }

    private PixelBuffer(ImageSize size, byte[] pixels)
    {
        Size = size;
        _pixels = pixels;
    }

    /// <summary>The dimensions of the buffer.</summary>
    public ImageSize Size { get; }

    /// <summary>The width in pixels.</summary>
    public int Width => Size.Width;

    /// <summary>The height in pixels.</summary>
    public int Height => Size.Height;

    /// <summary>The rectangle covering the whole buffer, anchored at (0, 0).</summary>
    public ImageRect Bounds => Size.ToRect();

    /// <summary>
    /// Wraps an existing byte array without copying it. The array must be exactly
    /// <c>width * height * <see cref="BytesPerPixel"/></c> bytes long and laid out as
    /// described in the type remarks.
    /// </summary>
    /// <param name="size">The dimensions the data represents.</param>
    /// <param name="pixels">The raw pixel data; ownership transfers to the buffer.</param>
    /// <returns>A buffer backed directly by <paramref name="pixels"/>.</returns>
    /// <exception cref="ArgumentException">The array length does not match the size.</exception>
    /// <remarks>
    /// This exists so decoders can hand over the array they already filled rather than
    /// paying for a second full-image copy. Callers must not retain a reference to
    /// <paramref name="pixels"/> afterwards.
    /// </remarks>
    public static PixelBuffer FromRawPixels(ImageSize size, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);

        long expected = size.PixelCount * BytesPerPixel;
        if (pixels.LongLength != expected)
        {
            throw new ArgumentException(
                $"Expected {expected} bytes for a {size} RGBA buffer but received {pixels.LongLength}.",
                nameof(pixels));
        }

        return new PixelBuffer(size, pixels);
    }

    /// <summary>Gets a writable view over the entire raw pixel data.</summary>
    /// <returns>A span covering every byte of the buffer.</returns>
    public Span<byte> AsSpan() => _pixels;

    /// <summary>Gets a read-only view over the entire raw pixel data.</summary>
    /// <returns>A read-only span covering every byte of the buffer.</returns>
    public ReadOnlySpan<byte> AsReadOnlySpan() => _pixels;

    /// <summary>Gets a writable view over a single row of pixels.</summary>
    /// <param name="y">The zero-based row index.</param>
    /// <returns>A span of <c>Width * BytesPerPixel</c> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The row is outside the buffer.</exception>
    public Span<byte> GetRowSpan(int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(y);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(y, Height);

        int stride = Width * BytesPerPixel;
        return _pixels.AsSpan(y * stride, stride);
    }

    /// <summary>Gets a read-only view over a single row of pixels.</summary>
    /// <param name="y">The zero-based row index.</param>
    /// <returns>A read-only span of <c>Width * BytesPerPixel</c> bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The row is outside the buffer.</exception>
    public ReadOnlySpan<byte> GetReadOnlyRowSpan(int y) => GetRowSpan(y);

    /// <summary>Reads the colour of a pixel.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns>The colour at that coordinate.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate lies outside the buffer.</exception>
    public PixelColor GetPixel(int x, int y)
    {
        if (!Size.Contains(x, y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside a {Size} buffer.");
        }

        int i = ByteIndex(x, y);
        return new PixelColor(_pixels[i], _pixels[i + 1], _pixels[i + 2], _pixels[i + 3]);
    }

    /// <summary>
    /// Reads the colour of a pixel, returning <see cref="PixelColor.Transparent"/> for
    /// coordinates outside the buffer instead of throwing.
    /// </summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns>The colour, or transparent if out of bounds.</returns>
    /// <remarks>
    /// Drawing tools rely on this: a brush stamped near an edge legitimately reaches past
    /// the canvas, and treating the outside as transparent is far simpler than clipping
    /// every read site.
    /// </remarks>
    public PixelColor GetPixelOrTransparent(int x, int y) =>
        Size.Contains(x, y) ? GetPixel(x, y) : PixelColor.Transparent;

    /// <summary>Overwrites a pixel, replacing it entirely rather than blending.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <param name="color">The colour to store.</param>
    /// <exception cref="ArgumentOutOfRangeException">The coordinate lies outside the buffer.</exception>
    /// <remarks>
    /// Replacement — not blending — is the correct default for pixel art: an artist placing
    /// a semi-transparent pixel expects exactly that value, not an accumulation of repeated
    /// strokes. Use <see cref="BlendPixel"/> where blending is genuinely wanted.
    /// </remarks>
    public void SetPixel(int x, int y, PixelColor color)
    {
        if (!Size.Contains(x, y))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"({x}, {y}) is outside a {Size} buffer.");
        }

        WriteUnchecked(ByteIndex(x, y), color);
    }

    /// <summary>
    /// Overwrites a pixel if the coordinate is inside the buffer, and does nothing if it is
    /// not.
    /// </summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <param name="color">The colour to store.</param>
    /// <returns><see langword="true"/> if a pixel was written.</returns>
    public bool TrySetPixel(int x, int y, PixelColor color)
    {
        if (!Size.Contains(x, y))
        {
            return false;
        }

        WriteUnchecked(ByteIndex(x, y), color);
        return true;
    }

    /// <summary>
    /// Composites <paramref name="color"/> over the existing pixel using straight-alpha
    /// source-over blending.
    /// </summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <param name="color">The source colour to blend on top.</param>
    /// <returns><see langword="true"/> if a pixel was written.</returns>
    public bool BlendPixel(int x, int y, PixelColor color)
    {
        if (!Size.Contains(x, y))
        {
            return false;
        }

        int i = ByteIndex(x, y);
        PixelColor destination = new(_pixels[i], _pixels[i + 1], _pixels[i + 2], _pixels[i + 3]);
        WriteUnchecked(i, ColorBlending.SourceOver(color, destination));
        return true;
    }

    /// <summary>Fills the whole buffer with a single colour.</summary>
    /// <param name="color">The colour to write to every pixel.</param>
    public void Fill(PixelColor color) => FillRect(Bounds, color);

    /// <summary>Resets every pixel to fully transparent black.</summary>
    public void Clear() => Array.Clear(_pixels);

    /// <summary>
    /// Fills a rectangular region with a single colour. The region is clipped to the
    /// buffer, so an oversized or partially off-canvas rectangle is safe.
    /// </summary>
    /// <param name="region">The region to fill, in buffer coordinates.</param>
    /// <param name="color">The colour to write.</param>
    public void FillRect(ImageRect region, PixelColor color)
    {
        ImageRect clipped = region.Intersect(Bounds);
        if (clipped.IsEmpty)
        {
            return;
        }

        for (int y = clipped.Top; y < clipped.Bottom; y++)
        {
            int rowStart = ByteIndex(clipped.Left, y);
            for (int x = 0; x < clipped.Width; x++)
            {
                WriteUnchecked(rowStart + (x * BytesPerPixel), color);
            }
        }
    }

    /// <summary>Creates an independent copy of this buffer.</summary>
    /// <returns>A new buffer with identical dimensions and contents.</returns>
    public PixelBuffer Clone() => new(Size, (byte[])_pixels.Clone());

    /// <summary>
    /// Copies a region of this buffer into <paramref name="destination"/> at the given
    /// position, replacing the destination pixels. Both the source region and the
    /// destination placement are clipped, so partially overlapping copies are safe.
    /// </summary>
    /// <param name="sourceRegion">The region of this buffer to copy.</param>
    /// <param name="destination">The buffer to copy into.</param>
    /// <param name="destinationOrigin">Where the region's top-left corner lands.</param>
    public void CopyRegionTo(ImageRect sourceRegion, PixelBuffer destination, ImagePoint destinationOrigin)
    {
        ArgumentNullException.ThrowIfNull(destination);

        ImageRect source = sourceRegion.Intersect(Bounds);
        if (source.IsEmpty)
        {
            return;
        }

        // Clip against the destination as well, translating the result back into source
        // coordinates so both sides stay aligned.
        int offsetX = destinationOrigin.X - source.X;
        int offsetY = destinationOrigin.Y - source.Y;
        ImageRect destinationRect = source.Translate(offsetX, offsetY).Intersect(destination.Bounds);
        if (destinationRect.IsEmpty)
        {
            return;
        }

        source = destinationRect.Translate(-offsetX, -offsetY);

        int rowBytes = source.Width * BytesPerPixel;
        for (int row = 0; row < source.Height; row++)
        {
            ReadOnlySpan<byte> from = _pixels.AsSpan(ByteIndex(source.Left, source.Top + row), rowBytes);
            Span<byte> to = destination._pixels.AsSpan(
                destination.ByteIndex(destinationRect.Left, destinationRect.Top + row),
                rowBytes);
            from.CopyTo(to);
        }
    }

    /// <summary>
    /// Extracts a region into a compact byte array laid out as a standalone image of that
    /// region's size. Used by the undo system to snapshot only the pixels an edit touched.
    /// </summary>
    /// <param name="region">The region to extract; must lie inside the buffer.</param>
    /// <returns>The region's pixel data, tightly packed.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The region is not fully inside the buffer.</exception>
    public byte[] ExtractRegion(ImageRect region)
    {
        if (!Bounds.Contains(region))
        {
            throw new ArgumentOutOfRangeException(nameof(region), $"{region} is not inside a {Size} buffer.");
        }

        int rowBytes = region.Width * BytesPerPixel;
        byte[] result = new byte[(long)rowBytes * region.Height];

        for (int row = 0; row < region.Height; row++)
        {
            _pixels.AsSpan(ByteIndex(region.Left, region.Top + row), rowBytes)
                   .CopyTo(result.AsSpan(row * rowBytes, rowBytes));
        }

        return result;
    }

    /// <summary>
    /// Writes back a region previously produced by <see cref="ExtractRegion"/>, replacing
    /// the buffer's contents in that rectangle. This is how an undo step is applied.
    /// </summary>
    /// <param name="region">The region to overwrite; must lie inside the buffer.</param>
    /// <param name="pixels">Tightly packed pixel data matching the region's size.</param>
    /// <exception cref="ArgumentOutOfRangeException">The region is not fully inside the buffer.</exception>
    /// <exception cref="ArgumentException">The data length does not match the region.</exception>
    public void ReplaceRegion(ImageRect region, ReadOnlySpan<byte> pixels)
    {
        if (!Bounds.Contains(region))
        {
            throw new ArgumentOutOfRangeException(nameof(region), $"{region} is not inside a {Size} buffer.");
        }

        int rowBytes = region.Width * BytesPerPixel;
        if (pixels.Length != rowBytes * region.Height)
        {
            throw new ArgumentException(
                $"Expected {rowBytes * region.Height} bytes for region {region} but received {pixels.Length}.",
                nameof(pixels));
        }

        for (int row = 0; row < region.Height; row++)
        {
            pixels.Slice(row * rowBytes, rowBytes)
                  .CopyTo(_pixels.AsSpan(ByteIndex(region.Left, region.Top + row), rowBytes));
        }
    }

    /// <summary>
    /// Determines whether every pixel in this buffer equals the corresponding pixel of
    /// <paramref name="other"/>. Primarily a testing and change-detection aid.
    /// </summary>
    /// <param name="other">The buffer to compare against.</param>
    /// <returns><see langword="true"/> if both buffers have the same size and contents.</returns>
    public bool ContentEquals(PixelBuffer? other) =>
        other is not null && Size == other.Size && _pixels.AsSpan().SequenceEqual(other._pixels);

    private int ByteIndex(int x, int y) => ((y * Width) + x) * BytesPerPixel;

    private void WriteUnchecked(int byteIndex, PixelColor color)
    {
        _pixels[byteIndex] = color.R;
        _pixels[byteIndex + 1] = color.G;
        _pixels[byteIndex + 2] = color.B;
        _pixels[byteIndex + 3] = color.A;
    }
}
