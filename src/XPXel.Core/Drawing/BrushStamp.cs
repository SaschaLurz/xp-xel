using XPXel.Core.Primitives;

namespace XPXel.Core.Drawing;

/// <summary>The outline shape of a brush.</summary>
public enum BrushShape
{
    /// <summary>
    /// A square block of pixels. The default, because it tiles exactly on the pixel grid and
    /// gives predictable results at every size — which is what pixel art needs.
    /// </summary>
    Square = 0,

    /// <summary>A filled circle, useful at larger sizes for organic shapes.</summary>
    Circle = 1,
}

/// <summary>
/// Expands a brush size and shape into the set of pixels it covers around a point.
/// </summary>
/// <remarks>
/// <para>
/// Keeping the stamp geometry in one place means the pencil, the eraser and any future
/// brush-based tool all produce identically shaped marks, and a new brush shape becomes a
/// single new case here rather than an edit to every tool.
/// </para>
/// <para>
/// <b>Even sizes.</b> A brush of even width cannot be centred on a pixel, so the extra
/// column and row go down and to the right. This matches the behaviour of other pixel
/// editors and keeps a size-2 brush aligned with a 2x2 grid.
/// </para>
/// </remarks>
public static class BrushStamp
{
    /// <summary>
    /// Invokes <paramref name="plot"/> once for every pixel the brush covers when centred on
    /// the given point.
    /// </summary>
    /// <param name="center">Where the brush is centred, in image coordinates.</param>
    /// <param name="size">The brush width in pixels; values below 1 are treated as 1.</param>
    /// <param name="shape">The brush outline.</param>
    /// <param name="plot">Receives the x and y of each covered pixel.</param>
    public static void Stamp(ImagePoint center, int size, BrushShape shape, Action<int, int> plot)
    {
        ArgumentNullException.ThrowIfNull(plot);

        if (size <= 1)
        {
            plot(center.X, center.Y);
            return;
        }

        int start = -((size - 1) / 2);
        int end = size / 2;

        if (shape == BrushShape.Square)
        {
            for (int dy = start; dy <= end; dy++)
            {
                for (int dx = start; dx <= end; dx++)
                {
                    plot(center.X + dx, center.Y + dy);
                }
            }

            return;
        }

        // A circle is measured from the geometric centre of the covered block, which for an
        // even size falls on a pixel boundary rather than a pixel.
        double centerOffset = (start + end) / 2.0;
        double radius = size / 2.0;
        double radiusSquared = radius * radius;

        for (int dy = start; dy <= end; dy++)
        {
            double ny = dy - centerOffset;
            for (int dx = start; dx <= end; dx++)
            {
                double nx = dx - centerOffset;
                if ((nx * nx) + (ny * ny) <= radiusSquared)
                {
                    plot(center.X + dx, center.Y + dy);
                }
            }
        }
    }

    /// <summary>
    /// Invokes <paramref name="plot"/> for every pixel the brush covers, reporting how much
    /// of each pixel the brush actually covers.
    /// </summary>
    /// <param name="center">Where the brush is centred, in image coordinates.</param>
    /// <param name="size">The brush width in pixels; values below 1 are treated as 1.</param>
    /// <param name="shape">The brush outline.</param>
    /// <param name="softness">
    /// How far in from the rim the brush fades, from 0 (a hard edge, identical to
    /// <see cref="Stamp"/>) to 1 (a fade starting at the very centre).
    /// </param>
    /// <param name="plot">Receives the x, the y and the coverage — greater than 0, up to 1.</param>
    /// <remarks>
    /// <para>
    /// Coverage is a <em>brush</em> value, not an anti-aliasing value: it is what makes a
    /// soft round brush soft, and it is deliberately unavailable to the pencil, whose whole
    /// job is to put one exact colour in one exact pixel.
    /// </para>
    /// <para>
    /// The falloff is linear between the inner and outer radius. Something smoother — a
    /// smoothstep or a gaussian — reads almost identically once quantised to 256 alpha levels
    /// on a brush a few pixels across, which is the size pixel art actually uses.
    /// </para>
    /// </remarks>
    public static void StampSoft(
        ImagePoint center,
        int size,
        BrushShape shape,
        double softness,
        Action<int, int, double> plot)
    {
        ArgumentNullException.ThrowIfNull(plot);

        if (size <= 1 || softness <= 0.0)
        {
            // A hard brush is the common case and needs none of the arithmetic below.
            Stamp(center, size, shape, (x, y) => plot(x, y, 1.0));
            return;
        }

        int start = -((size - 1) / 2);
        int end = size / 2;

        double centerOffset = (start + end) / 2.0;
        double outerRadius = size / 2.0;
        double innerRadius = outerRadius * (1.0 - Math.Clamp(softness, 0.0, 1.0));
        double falloff = outerRadius - innerRadius;

        for (int dy = start; dy <= end; dy++)
        {
            double ny = dy - centerOffset;

            for (int dx = start; dx <= end; dx++)
            {
                double nx = dx - centerOffset;

                // A square brush fades towards its edges, not towards its corners, so its
                // distance is measured along whichever axis is furthest out.
                double distance = shape == BrushShape.Square
                    ? Math.Max(Math.Abs(nx), Math.Abs(ny))
                    : Math.Sqrt((nx * nx) + (ny * ny));

                if (distance > outerRadius)
                {
                    continue;
                }

                double coverage = distance <= innerRadius ? 1.0 : (outerRadius - distance) / falloff;
                if (coverage > 0.0)
                {
                    plot(center.X + dx, center.Y + dy, Math.Min(1.0, coverage));
                }
            }
        }
    }

    /// <summary>
    /// Returns the rectangle a brush of the given size covers when centred on a point. Used
    /// to compute dirty regions without stamping.
    /// </summary>
    /// <param name="center">Where the brush is centred.</param>
    /// <param name="size">The brush width in pixels.</param>
    /// <returns>The covered rectangle.</returns>
    public static ImageRect GetBounds(ImagePoint center, int size)
    {
        if (size <= 1)
        {
            return new ImageRect(center.X, center.Y, 1, 1);
        }

        int start = -((size - 1) / 2);
        int end = size / 2;
        return ImageRect.FromEdges(center.X + start, center.Y + start, center.X + end + 1, center.Y + end + 1);
    }
}
