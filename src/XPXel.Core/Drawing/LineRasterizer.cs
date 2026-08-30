using XPXel.Core.Primitives;

namespace XPXel.Core.Drawing;

/// <summary>
/// Converts a line between two pixel coordinates into the set of pixels that line covers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is needed at all.</b> Pointer input arrives as discrete samples. Moving the
/// mouse quickly across the canvas can jump twenty pixels between two events, and stamping
/// only at the reported positions leaves a dotted trail instead of a stroke. Every stroke
/// tool therefore joins consecutive samples with a line.
/// </para>
/// <para>
/// The implementation is Bresenham's integer algorithm: no floating point, no
/// anti-aliasing, and exactly one pixel per step. That is precisely the behaviour pixel art
/// wants — a hand-placeable, countable set of pixels — which is why editors of this kind do
/// not use the smooth curve rasterizers a vector tool would.
/// </para>
/// </remarks>
public static class LineRasterizer
{
    /// <summary>
    /// Invokes <paramref name="plot"/> for each pixel on the line from
    /// <paramref name="from"/> to <paramref name="to"/>, inclusive of both endpoints.
    /// </summary>
    /// <param name="from">The first endpoint.</param>
    /// <param name="to">The second endpoint.</param>
    /// <param name="plot">Receives the x and y of each pixel along the line.</param>
    public static void PlotLine(ImagePoint from, ImagePoint to, Action<int, int> plot)
    {
        ArgumentNullException.ThrowIfNull(plot);

        int x = from.X;
        int y = from.Y;

        int deltaX = Math.Abs(to.X - x);
        int deltaY = -Math.Abs(to.Y - y);
        int stepX = x < to.X ? 1 : -1;
        int stepY = y < to.Y ? 1 : -1;
        int error = deltaX + deltaY;

        while (true)
        {
            plot(x, y);

            if (x == to.X && y == to.Y)
            {
                return;
            }

            // Doubling the error before comparing keeps everything in integers; this is the
            // standard formulation of the algorithm.
            int doubledError = error * 2;

            if (doubledError >= deltaY)
            {
                error += deltaY;
                x += stepX;
            }

            if (doubledError <= deltaX)
            {
                error += deltaX;
                y += stepY;
            }
        }
    }
}
