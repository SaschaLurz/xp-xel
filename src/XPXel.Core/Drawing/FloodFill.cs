using XPXel.Core.Imaging;
using XPXel.Core.Primitives;

namespace XPXel.Core.Drawing;

/// <summary>
/// Finds the pixels a bucket fill should affect.
/// </summary>
/// <remarks>
/// <para>
/// <b>Separation of concerns.</b> This class decides <em>which</em> pixels to fill and never
/// writes any. The caller supplies a callback that does the writing, which is what lets the
/// fill go through a <c>RasterEditSession</c> and become undoable without the algorithm
/// knowing anything about the history system.
/// </para>
/// <para>
/// <b>Why a visited array rather than checking the buffer.</b> The obvious implementation
/// re-reads each pixel to see whether it has already been filled. That breaks in two real
/// cases: filling with the colour that is already there (nothing ever changes, so the
/// algorithm never terminates), and filling inside a selection (rejected writes look
/// unvisited). Tracking visits explicitly costs one byte per pixel and is always correct.
/// </para>
/// <para>
/// The scan-line variant is used rather than naive four-way recursion: it processes whole
/// horizontal runs at a time, which cuts the work stack from potentially one entry per pixel
/// to one per run and avoids the stack overflow that recursive flood fills hit on large
/// areas.
/// </para>
/// </remarks>
public static class FloodFill
{
    /// <summary>
    /// Fills the region matching the colour at <paramref name="start"/>.
    /// </summary>
    /// <param name="buffer">The buffer to read colours from.</param>
    /// <param name="start">The clicked pixel, whose colour defines what "matching" means.</param>
    /// <param name="tolerance">
    /// How far a pixel may differ per channel and still match, 0 to 255.
    /// </param>
    /// <param name="contiguous">
    /// <see langword="true"/> to fill only the connected region containing
    /// <paramref name="start"/>; <see langword="false"/> to fill every matching pixel in the
    /// buffer.
    /// </param>
    /// <param name="canVisit">
    /// An optional gate, used to honour a selection. Pixels it rejects are neither filled
    /// nor traversed, so a selection boundary also stops the fill from spreading.
    /// </param>
    /// <param name="fillPixel">Called once for each pixel that should be filled.</param>
    public static void Fill(
        PixelBuffer buffer,
        ImagePoint start,
        int tolerance,
        bool contiguous,
        Func<int, int, bool>? canVisit,
        Action<int, int> fillPixel)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentNullException.ThrowIfNull(fillPixel);

        if (!buffer.Size.Contains(start))
        {
            return;
        }

        PixelColor seed = buffer.GetPixel(start.X, start.Y);

        if (!contiguous)
        {
            FillGlobally(buffer, seed, tolerance, canVisit, fillPixel);
            return;
        }

        FillContiguously(buffer, start, seed, tolerance, canVisit, fillPixel);
    }

    private static void FillGlobally(
        PixelBuffer buffer,
        PixelColor seed,
        int tolerance,
        Func<int, int, bool>? canVisit,
        Action<int, int> fillPixel)
    {
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                if (buffer.GetPixel(x, y).IsSimilarTo(seed, tolerance) && (canVisit?.Invoke(x, y) ?? true))
                {
                    fillPixel(x, y);
                }
            }
        }
    }

    private static void FillContiguously(
        PixelBuffer buffer,
        ImagePoint start,
        PixelColor seed,
        int tolerance,
        Func<int, int, bool>? canVisit,
        Action<int, int> fillPixel)
    {
        int width = buffer.Width;
        int height = buffer.Height;
        bool[] visited = new bool[buffer.Size.PixelCount];

        bool Matches(int x, int y) =>
            !visited[(y * width) + x]
            && buffer.GetPixel(x, y).IsSimilarTo(seed, tolerance)
            && (canVisit?.Invoke(x, y) ?? true);

        if (!Matches(start.X, start.Y))
        {
            return;
        }

        Stack<ImagePoint> pending = new();
        pending.Push(start);

        while (pending.Count > 0)
        {
            ImagePoint point = pending.Pop();
            int y = point.Y;

            if (!Matches(point.X, y))
            {
                continue;
            }

            // Grow the run outwards from the seed point in both directions.
            int left = point.X;
            while (left - 1 >= 0 && Matches(left - 1, y))
            {
                left--;
            }

            int right = point.X;
            while (right + 1 < width && Matches(right + 1, y))
            {
                right++;
            }

            // Fill the run, then look for new runs on the rows immediately above and below.
            // Marking visited as we go stops the neighbour scan from re-queueing this row.
            for (int x = left; x <= right; x++)
            {
                visited[(y * width) + x] = true;
                fillPixel(x, y);
            }

            for (int x = left; x <= right; x++)
            {
                if (y > 0 && Matches(x, y - 1))
                {
                    pending.Push(new ImagePoint(x, y - 1));
                }

                if (y + 1 < height && Matches(x, y + 1))
                {
                    pending.Push(new ImagePoint(x, y + 1));
                }
            }
        }
    }
}
