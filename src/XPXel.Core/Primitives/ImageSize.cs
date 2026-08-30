namespace XPXel.Core.Primitives;

/// <summary>
/// The integer dimensions, in pixels, of a document, layer buffer or region.
/// </summary>
/// <remarks>
/// Sizes are always non-negative. A size with a zero component is "empty" and is legal —
/// it represents, for example, the bounds of a stroke that has not touched any pixel yet.
/// See <see cref="ImagePoint"/> for why these types are named <c>Image*</c> rather than
/// <c>Pixel*</c>.
/// </remarks>
public readonly struct ImageSize : IEquatable<ImageSize>
{
    /// <summary>Initialises a new size.</summary>
    /// <param name="width">The width in pixels; must not be negative.</param>
    /// <param name="height">The height in pixels; must not be negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative.</exception>
    public ImageSize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        Width = width;
        Height = height;
    }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>A size of zero by zero pixels.</summary>
    public static ImageSize Empty => default;

    /// <summary>Gets a value indicating whether either dimension is zero.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>
    /// The total number of pixels. Computed as a <see cref="long"/> so that very large
    /// canvases cannot silently overflow when callers size buffers from it.
    /// </summary>
    public long PixelCount => (long)Width * Height;

    /// <summary>Determines whether the given coordinate lies inside a region of this size.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns><see langword="true"/> if the coordinate is within bounds.</returns>
    public bool Contains(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height;

    /// <summary>Determines whether the given point lies inside a region of this size.</summary>
    /// <param name="point">The point to test.</param>
    /// <returns><see langword="true"/> if the point is within bounds.</returns>
    public bool Contains(ImagePoint point) => Contains(point.X, point.Y);

    /// <summary>Returns the rectangle covering the whole area, anchored at the origin.</summary>
    /// <returns>A rectangle from (0, 0) with these dimensions.</returns>
    public ImageRect ToRect() => new(0, 0, Width, Height);

    /// <inheritdoc />
    public bool Equals(ImageSize other) => Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ImageSize other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Width, Height);

    /// <summary>Determines whether two sizes are equal.</summary>
    /// <param name="left">The first size.</param>
    /// <param name="right">The second size.</param>
    /// <returns><see langword="true"/> if the sizes are equal.</returns>
    public static bool operator ==(ImageSize left, ImageSize right) => left.Equals(right);

    /// <summary>Determines whether two sizes differ.</summary>
    /// <param name="left">The first size.</param>
    /// <param name="right">The second size.</param>
    /// <returns><see langword="true"/> if the sizes differ.</returns>
    public static bool operator !=(ImageSize left, ImageSize right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"{Width}x{Height}";
}
