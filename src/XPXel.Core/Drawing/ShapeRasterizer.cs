using XPXel.Core.Primitives;

namespace XPXel.Core.Drawing;

/// <summary>
/// Converts rectangles and ellipses into the set of pixels they cover.
/// </summary>
/// <remarks>
/// <para>
/// The counterpart to <see cref="LineRasterizer"/>, and written on the same principle:
/// integer arithmetic, no anti-aliasing, exactly one pixel per step. A pixel artist needs to
/// be able to count the pixels in a shape and get the same shape twice from the same drag.
/// </para>
/// <para>
/// <b>Every method may plot a pixel more than once.</b> The ellipse algorithm walks four
/// symmetric octants that meet at the poles, and a one-pixel-tall ellipse is entirely poles.
/// De-duplicating here would need a set per shape; callers write through a
/// <see cref="History.RasterEditSession"/>, which ignores a write that changes nothing, so
/// the duplicates cost nothing and the code stays simple.
/// </para>
/// </remarks>
public static class ShapeRasterizer
{
    /// <summary>Plots a rectangle.</summary>
    /// <param name="rect">The rectangle in image coordinates.</param>
    /// <param name="isFilled">Whether to fill it or to draw a one-pixel outline.</param>
    /// <param name="plot">Receives the x and y of each covered pixel.</param>
    public static void PlotRectangle(ImageRect rect, bool isFilled, Action<int, int> plot)
    {
        ArgumentNullException.ThrowIfNull(plot);

        if (rect.IsEmpty)
        {
            return;
        }

        if (isFilled)
        {
            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    plot(x, y);
                }
            }

            return;
        }

        int right = rect.Right - 1;
        int bottom = rect.Bottom - 1;

        for (int x = rect.Left; x <= right; x++)
        {
            plot(x, rect.Top);
            plot(x, bottom);
        }

        for (int y = rect.Top; y <= bottom; y++)
        {
            plot(rect.Left, y);
            plot(right, y);
        }
    }

    /// <summary>Plots an ellipse inscribed in a rectangle.</summary>
    /// <param name="rect">The bounding rectangle in image coordinates.</param>
    /// <param name="isFilled">Whether to fill it or to draw a one-pixel outline.</param>
    /// <param name="plot">Receives the x and y of each covered pixel.</param>
    /// <remarks>
    /// <para>
    /// The midpoint ellipse algorithm generalised to even diameters. The usual textbook form
    /// assumes an ellipse centred on a pixel, which can only produce odd widths; a pixel
    /// editor has to be able to draw a 4×4 circle, so the centre is allowed to fall on a pixel
    /// boundary and the two halves are mirrored about it.
    /// </para>
    /// <para>
    /// <b>The two vertical cursors move apart, not down.</b> After the initial step both start
    /// at the ellipse's vertical centre; <c>lower</c> walks downwards and <c>upper</c> upwards,
    /// which is what makes the four quadrants come out symmetric. Reading them as "top" and
    /// "bottom" is the natural mistake here and it inverts the loop conditions.
    /// </para>
    /// </remarks>
    public static void PlotEllipse(ImageRect rect, bool isFilled, Action<int, int> plot)
    {
        ArgumentNullException.ThrowIfNull(plot);

        if (rect.IsEmpty)
        {
            return;
        }

        int left = rect.Left;
        int right = rect.Right - 1;

        long a = right - left;
        long b = rect.Bottom - 1 - rect.Top;
        long b1 = b & 1;

        long dx = 4 * (1 - a) * b * b;
        long dy = 4 * (b1 + 1) * a * a;
        long error = dx + dy + (b1 * a * a);

        // Both cursors start at the vertical centre and move apart from there.
        int lower = rect.Top + (int)((b + 1) / 2);
        int upper = lower - (int)b1;

        long aa8 = 8 * a * a;
        long bb8 = 8 * b * b;

        do
        {
            if (isFilled)
            {
                // One span per row pair rather than four points, so a filled ellipse is not
                // drawn by overlapping every interior pixel once per column.
                for (int x = left; x <= right; x++)
                {
                    plot(x, lower);
                    plot(x, upper);
                }
            }
            else
            {
                plot(right, lower);
                plot(left, lower);
                plot(left, upper);
                plot(right, upper);
            }

            long doubledError = 2 * error;

            if (doubledError <= dy)
            {
                lower++;
                upper--;
                dy += aa8;
                error += dy;
            }

            if (doubledError >= dx || 2 * error > dy)
            {
                left++;
                right--;
                dx += bb8;
                error += dx;
            }
        }
        while (left <= right);

        // A very flat ellipse — one only a pixel or two tall — has its columns cross before
        // the ends are reached, leaving the tips unpainted. Walking the remaining rows closes
        // them.
        //
        // The guard is deliberate. The loop is bounded by the ellipse's own height by
        // construction, but this rasteriser runs directly off a pointer drag, and an editor
        // that can be frozen by a mouse gesture is a far worse bug than a slightly wrong
        // ellipse. An earlier version of this loop had its condition the wrong way round and
        // spun for two billion iterations on the first ellipse anyone drew.
        for (long steps = 0; lower - upper <= b && steps <= b + 2; steps++)
        {
            plot(left - 1, lower);
            plot(right + 1, lower);
            lower++;

            plot(left - 1, upper);
            plot(right + 1, upper);
            upper--;
        }
    }

    /// <summary>
    /// Snaps a dragged rectangle to a square, keeping the corner the drag started from
    /// fixed. This is what Shift does with the rectangle and ellipse tools.
    /// </summary>
    /// <param name="anchor">Where the drag started.</param>
    /// <param name="current">Where the pointer is now.</param>
    /// <returns>The constrained pointer position.</returns>
    public static ImagePoint ConstrainToSquare(ImagePoint anchor, ImagePoint current)
    {
        int dx = current.X - anchor.X;
        int dy = current.Y - anchor.Y;
        int size = Math.Max(Math.Abs(dx), Math.Abs(dy));

        return new ImagePoint(
            anchor.X + (dx < 0 ? -size : size),
            anchor.Y + (dy < 0 ? -size : size));
    }

    /// <summary>
    /// Snaps a dragged line to the nearest 45° step. This is what Shift does with the line
    /// tool, and it is the reason a shift-drag can draw an exactly horizontal edge.
    /// </summary>
    /// <param name="anchor">Where the drag started.</param>
    /// <param name="current">Where the pointer is now.</param>
    /// <returns>The constrained pointer position.</returns>
    public static ImagePoint ConstrainToAxis(ImagePoint anchor, ImagePoint current)
    {
        int dx = current.X - anchor.X;
        int dy = current.Y - anchor.Y;
        int absX = Math.Abs(dx);
        int absY = Math.Abs(dy);

        // Within an octant of the diagonal, snap to the diagonal; otherwise to the nearer axis.
        if (Math.Abs(absX - absY) <= Math.Max(absX, absY) / 2)
        {
            int size = Math.Max(absX, absY);
            return new ImagePoint(
                anchor.X + (dx < 0 ? -size : size),
                anchor.Y + (dy < 0 ? -size : size));
        }

        return absX > absY
            ? new ImagePoint(current.X, anchor.Y)
            : new ImagePoint(anchor.X, current.Y);
    }
}
