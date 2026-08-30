namespace XPXel.Core.Primitives;

/// <summary>
/// An integer coordinate in <em>image space</em>: the discrete pixel grid of a document,
/// layer or buffer, where (0, 0) is the top-left pixel and Y increases downwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this type exists instead of Avalonia's <c>PixelPoint</c>:</b> XPXel.Core must not
/// reference any UI framework (see the project header of XPXel.Core.csproj). The names
/// <c>Image*</c> rather than <c>Pixel*</c> were chosen deliberately so that UI code can
/// import both <c>Avalonia</c> and <c>XPXel.Core.Primitives</c> without ambiguity errors.
/// </para>
/// <para>
/// Image space is distinct from <em>view space</em>, which is the zoomed and panned
/// on-screen coordinate system. Conversion between the two is the sole responsibility of
/// the canvas control in XPXel.UI; nothing in Core ever deals with view space.
/// </para>
/// </remarks>
public readonly struct ImagePoint : IEquatable<ImagePoint>
{
    /// <summary>Initialises a new point at the given image-space coordinate.</summary>
    /// <param name="x">The horizontal coordinate, increasing to the right.</param>
    /// <param name="y">The vertical coordinate, increasing downwards.</param>
    public ImagePoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    /// <summary>The horizontal coordinate, increasing to the right.</summary>
    public int X { get; }

    /// <summary>The vertical coordinate, increasing downwards.</summary>
    public int Y { get; }

    /// <summary>The top-left origin of image space, (0, 0).</summary>
    public static ImagePoint Origin => default;

    /// <summary>Returns a copy of this point translated by the given deltas.</summary>
    /// <param name="deltaX">Amount to add to <see cref="X"/>.</param>
    /// <param name="deltaY">Amount to add to <see cref="Y"/>.</param>
    /// <returns>The translated point.</returns>
    public ImagePoint Translate(int deltaX, int deltaY) => new(X + deltaX, Y + deltaY);

    /// <summary>Returns a copy of this point translated by another point treated as a vector.</summary>
    /// <param name="offset">The offset to apply.</param>
    /// <returns>The translated point.</returns>
    public ImagePoint Translate(ImagePoint offset) => new(X + offset.X, Y + offset.Y);

    /// <summary>Returns the vector from <paramref name="other"/> to this point.</summary>
    /// <param name="other">The point to subtract.</param>
    /// <returns>The difference, expressed as a point acting as a vector.</returns>
    public ImagePoint Subtract(ImagePoint other) => new(X - other.X, Y - other.Y);

    /// <inheritdoc />
    public bool Equals(ImagePoint other) => X == other.X && Y == other.Y;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ImagePoint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(X, Y);

    /// <summary>Determines whether two points refer to the same pixel.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns><see langword="true"/> if the points are equal.</returns>
    public static bool operator ==(ImagePoint left, ImagePoint right) => left.Equals(right);

    /// <summary>Determines whether two points refer to different pixels.</summary>
    /// <param name="left">The first point.</param>
    /// <param name="right">The second point.</param>
    /// <returns><see langword="true"/> if the points differ.</returns>
    public static bool operator !=(ImagePoint left, ImagePoint right) => !left.Equals(right);

    /// <inheritdoc />
    public override string ToString() => $"({X}, {Y})";
}
