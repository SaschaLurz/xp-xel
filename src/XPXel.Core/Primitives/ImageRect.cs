namespace XPXel.Core.Primitives;

/// <summary>
/// An axis-aligned rectangle of whole pixels in image space.
/// </summary>
/// <remarks>
/// <para>
/// This type is the workhorse of XP-xel's <em>dirty rectangle</em> strategy. Every edit
/// reports the region it touched, and the compositor, the undo system and the canvas
/// control all use those regions to avoid recomputing or re-uploading the whole image for
/// a one-pixel change. Getting this right is what keeps the editor responsive on large
/// canvases, so prefer passing a rect through the pipeline over recomputing bounds.
/// </para>
/// <para>
/// The rectangle is half-open: <see cref="Right"/> and <see cref="Bottom"/> are one past
/// the last contained pixel, which makes width/height arithmetic and loop bounds natural.
/// </para>
/// </remarks>
public readonly struct ImageRect : IEquatable<ImageRect>
{
    /// <summary>Initialises a rectangle from a position and a size.</summary>
    /// <param name="x">The left edge.</param>
    /// <param name="y">The top edge.</param>
    /// <param name="width">The width in pixels; must not be negative.</param>
    /// <param name="height">The height in pixels; must not be negative.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is negative.</exception>
    public ImageRect(int x, int y, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>Initialises a rectangle from a top-left corner and a size.</summary>
    /// <param name="origin">The top-left corner.</param>
    /// <param name="size">The dimensions.</param>
    public ImageRect(ImagePoint origin, ImageSize size)
        : this(origin.X, origin.Y, size.Width, size.Height)
    {
    }

    /// <summary>The left edge, inclusive.</summary>
    public int X { get; }

    /// <summary>The top edge, inclusive.</summary>
    public int Y { get; }

    /// <summary>The width in pixels.</summary>
    public int Width { get; }

    /// <summary>The height in pixels.</summary>
    public int Height { get; }

    /// <summary>The left edge, inclusive. Alias of <see cref="X"/>.</summary>
    public int Left => X;

    /// <summary>The top edge, inclusive. Alias of <see cref="Y"/>.</summary>
    public int Top => Y;

    /// <summary>The right edge, <em>exclusive</em> — one past the last contained column.</summary>
    public int Right => X + Width;

    /// <summary>The bottom edge, <em>exclusive</em> — one past the last contained row.</summary>
    public int Bottom => Y + Height;

    /// <summary>The top-left corner.</summary>
    public ImagePoint Origin => new(X, Y);

    /// <summary>The dimensions.</summary>
    public ImageSize Size => new(Width, Height);

    /// <summary>Gets a value indicating whether the rectangle covers no pixels.</summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>An empty rectangle at the origin, used as the identity value for <see cref="Union(ImageRect)"/>.</summary>
    public static ImageRect Empty => default;

    /// <summary>Creates a rectangle from edge coordinates rather than a size.</summary>
    /// <param name="left">The left edge, inclusive.</param>
    /// <param name="top">The top edge, inclusive.</param>
    /// <param name="right">The right edge, exclusive.</param>
    /// <param name="bottom">The bottom edge, exclusive.</param>
    /// <returns>The rectangle, clamped to empty if the edges are inverted.</returns>
    public static ImageRect FromEdges(int left, int top, int right, int bottom)
    {
        int width = Math.Max(0, right - left);
        int height = Math.Max(0, bottom - top);
        return new ImageRect(left, top, width, height);
    }

    /// <summary>Creates the smallest rectangle containing both given points.</summary>
    /// <param name="a">The first point.</param>
    /// <param name="b">The second point.</param>
    /// <returns>A rectangle that contains both points.</returns>
    public static ImageRect FromPoints(ImagePoint a, ImagePoint b) => FromEdges(
        Math.Min(a.X, b.X),
        Math.Min(a.Y, b.Y),
        Math.Max(a.X, b.X) + 1,
        Math.Max(a.Y, b.Y) + 1);

    /// <summary>Determines whether the given coordinate lies inside the rectangle.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns><see langword="true"/> if the coordinate is contained.</returns>
    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Determines whether the given point lies inside the rectangle.</summary>
    /// <param name="point">The point to test.</param>
    /// <returns><see langword="true"/> if the point is contained.</returns>
    public bool Contains(ImagePoint point) => Contains(point.X, point.Y);

    /// <summary>Determines whether this rectangle fully contains another.</summary>
    /// <param name="other">The rectangle to test.</param>
    /// <returns><see langword="true"/> if <paramref name="other"/> lies entirely inside this one.</returns>
    public bool Contains(ImageRect other) =>
        other.IsEmpty || (other.X >= X && other.Y >= Y && other.Right <= Right && other.Bottom <= Bottom);

    /// <summary>Determines whether this rectangle shares any pixel with another.</summary>
    /// <param name="other">The rectangle to test.</param>
    /// <returns><see langword="true"/> if the rectangles overlap.</returns>
    public bool IntersectsWith(ImageRect other) =>
        !IsEmpty && !other.IsEmpty &&
        other.X < Right && X < other.Right &&
        other.Y < Bottom && Y < other.Bottom;

    /// <summary>Returns the overlapping region of two rectangles.</summary>
    /// <param name="other">The rectangle to intersect with.</param>
    /// <returns>The shared region, or <see cref="Empty"/> if they do not overlap.</returns>
    public ImageRect Intersect(ImageRect other)
    {
        if (IsEmpty || other.IsEmpty)
        {
            return Empty;
        }

        return FromEdges(
            Math.Max(X, other.X),
            Math.Max(Y, other.Y),
            Math.Min(Right, other.Right),
            Math.Min(Bottom, other.Bottom));
    }

    /// <summary>
    /// Returns the smallest rectangle containing both rectangles. An empty operand is
    /// ignored, which lets callers start an accumulation from <see cref="Empty"/>.
    /// </summary>
    /// <param name="other">The rectangle to combine with.</param>
    /// <returns>The bounding rectangle of both.</returns>
    public ImageRect Union(ImageRect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return FromEdges(
            Math.Min(X, other.X),
            Math.Min(Y, other.Y),
            Math.Max(Right, other.Right),
            Math.Max(Bottom, other.Bottom));
    }

    /// <summary>Returns the smallest rectangle containing this rectangle and the given pixel.</summary>
    /// <param name="x">The horizontal coordinate to include.</param>
    /// <param name="y">The vertical coordinate to include.</param>
    /// <returns>The expanded rectangle.</returns>
    public ImageRect Union(int x, int y) => Union(new ImageRect(x, y, 1, 1));

    /// <summary>Returns a copy grown by <paramref name="amount"/> pixels on every side.</summary>
    /// <param name="amount">The number of pixels to grow by; may be negative to shrink.</param>
    /// <returns>The inflated rectangle, clamped to empty if it collapses.</returns>
    public ImageRect Inflate(int amount) =>
        FromEdges(X - amount, Y - amount, Right + amount, Bottom + amount);

    /// <summary>Returns a copy translated by the given deltas.</summary>
    /// <param name="deltaX">Horizontal translation.</param>
    /// <param name="deltaY">Vertical translation.</param>
    /// <returns>The translated rectangle.</returns>
    public ImageRect Translate(int deltaX, int deltaY) => new(X + deltaX, Y + deltaY, Width, Height);

    /// <inheritdoc />
    public bool Equals(ImageRect other) =>
        X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ImageRect other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);

    /// <summary>Determines whether two rectangles are equal.</summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns><see langword="true"/> if the rectangles are equal.</returns>
    public static bool operator ==(ImageRect left, ImageRect right) => left.Equals(right);

    /// <summary>Determines whether two rectangles differ.</summary>
    /// <param name="left">The first rectangle.</param>
    /// <param name="right">The second rectangle.</param>
    /// <returns><see langword="true"/> if the rectangles differ.</returns>
    public static bool operator !=(ImageRect left, ImageRect right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y}, {Width}x{Height})";
}
