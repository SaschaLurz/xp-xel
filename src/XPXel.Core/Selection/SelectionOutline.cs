using XPXel.Core.Primitives;

namespace XPXel.Core.Selection;

/// <summary>
/// Traces the edge of a selection: the lines that separate marked pixels from unmarked ones.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the bounding rectangle will not do.</b> Marching ants drawn around a mask's bounds
/// say "a box is selected", and for the rectangular marquee that happens to be true. For a mask
/// painted pixel by pixel, or one produced by a wand, it is a lie — and a costly one, because
/// the artist believes it: they see a box, drag it, and only the scattered pixels inside move.
/// The outline has to be the mask's own.
/// </para>
/// <para>
/// <b>Runs, not per-pixel segments.</b> Each edge is extended along its row or column for as far
/// as it continues, so a rectangular selection produces four lines rather than four per pixel.
/// A renderer that has to stroke one line per boundary pixel is what makes a selection tool feel
/// slow on a large canvas, and the merge costs one comparison per pixel.
/// </para>
/// <para>
/// Coordinates are grid lines, not pixel centres: the left edge of pixel (3, 0) is x = 3 and its
/// right edge is x = 4. That is the same convention <see cref="ImageRect"/> uses, so a caller
/// scales these by the zoom exactly as it scales a rectangle.
/// </para>
/// </remarks>
public static class SelectionOutline
{
    /// <summary>One straight run of the boundary, in grid-line coordinates.</summary>
    /// <param name="Start">Where the run begins.</param>
    /// <param name="End">Where it ends.</param>
    public readonly record struct Segment(ImagePoint Start, ImagePoint End);

    /// <summary>
    /// Traces every boundary between marked and unmarked pixels.
    /// </summary>
    /// <param name="mask">The mask to trace.</param>
    /// <returns>
    /// The runs making up the outline, horizontal ones first. Empty when nothing is marked.
    /// </returns>
    public static IReadOnlyList<Segment> Trace(SelectionMask? mask)
    {
        if (mask is null || mask.IsEmpty)
        {
            return [];
        }

        List<Segment> segments = [];
        ImageRect bounds = mask.Bounds;

        // Horizontal runs: a boundary above a pixel is a boundary below the one over it, so
        // each row is scanned for both its top and its bottom edges.
        for (int y = bounds.Top; y <= bounds.Bottom; y++)
        {
            AddRuns(
                segments,
                bounds.Left,
                bounds.Right,
                along => mask.IsSelected(along, y) != mask.IsSelected(along, y - 1),
                (from, to) => new Segment(new ImagePoint(from, y), new ImagePoint(to, y)));
        }

        // Vertical runs, the same scan turned on its side.
        for (int x = bounds.Left; x <= bounds.Right; x++)
        {
            AddRuns(
                segments,
                bounds.Top,
                bounds.Bottom,
                along => mask.IsSelected(x, along) != mask.IsSelected(x - 1, along),
                (from, to) => new Segment(new ImagePoint(x, from), new ImagePoint(x, to)));
        }

        return segments;
    }

    /// <summary>
    /// Walks one line of the grid, joining consecutive boundary crossings into single runs.
    /// </summary>
    /// <param name="segments">Where finished runs are collected.</param>
    /// <param name="first">The first coordinate to test along the line.</param>
    /// <param name="last">One past the last coordinate to test.</param>
    /// <param name="isEdge">Whether the boundary crosses this line at a given coordinate.</param>
    /// <param name="build">Turns a start and an end coordinate into a segment.</param>
    private static void AddRuns(
        List<Segment> segments,
        int first,
        int last,
        Func<int, bool> isEdge,
        Func<int, int, Segment> build)
    {
        int runStart = int.MinValue;

        for (int along = first; along <= last; along++)
        {
            // The extra step past the end closes a run that reaches the boundary of the scan.
            bool edge = along < last && isEdge(along);

            if (edge && runStart == int.MinValue)
            {
                runStart = along;
            }
            else if (!edge && runStart != int.MinValue)
            {
                segments.Add(build(runStart, along));
                runStart = int.MinValue;
            }
        }
    }
}
