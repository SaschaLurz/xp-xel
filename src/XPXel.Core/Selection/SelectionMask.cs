using XPXel.Core.Primitives;

namespace XPXel.Core.Selection;

/// <summary>
/// A per-pixel coverage mask restricting where edits may be applied.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists in v0.1 even though no tool creates one.</b> Selections change the
/// signature of every drawing operation: each tool has to ask "may I write to this pixel?"
/// before writing. Adding that question later would mean revisiting every tool. Instead the
/// question is asked from the start against a mask that is currently always
/// <see langword="null"/>, meaning "everything is selectable". Adding a marquee or lasso
/// tool then changes no existing tool code at all.
/// </para>
/// <para>
/// <b>Coverage, not a boolean.</b> Each pixel stores 0-255 rather than a flag so that
/// anti-aliased and feathered selections fit the same type. v0.1 treats any non-zero value
/// as fully selected; partial coverage will become an alpha multiplier when a tool that can
/// produce it exists.
/// </para>
/// </remarks>
public sealed class SelectionMask
{
    private readonly byte[] _coverage;

    /// <summary>Creates an empty mask — nothing selected — for a canvas of the given size.</summary>
    /// <param name="size">The canvas dimensions the mask applies to.</param>
    public SelectionMask(ImageSize size)
    {
        Size = size;
        _coverage = new byte[size.PixelCount];
        Bounds = ImageRect.Empty;
    }

    private SelectionMask(ImageSize size, byte[] coverage, ImageRect bounds)
    {
        Size = size;
        _coverage = coverage;
        Bounds = bounds;
    }

    /// <summary>The canvas dimensions this mask applies to.</summary>
    public ImageSize Size { get; }

    /// <summary>
    /// The bounding rectangle of every selected pixel, or an empty rectangle if nothing is
    /// selected. Tools use it to limit their iteration.
    /// </summary>
    public ImageRect Bounds { get; private set; }

    /// <summary>Gets a value indicating whether nothing at all is selected.</summary>
    public bool IsEmpty => Bounds.IsEmpty;

    /// <summary>
    /// Creates a mask selecting a single rectangle — what a rectangular marquee tool will
    /// produce.
    /// </summary>
    /// <param name="size">The canvas dimensions.</param>
    /// <param name="region">The region to select.</param>
    /// <returns>The new mask.</returns>
    public static SelectionMask FromRect(ImageSize size, ImageRect region)
    {
        SelectionMask mask = new(size);
        ImageRect clipped = region.Intersect(size.ToRect());

        for (int y = clipped.Top; y < clipped.Bottom; y++)
        {
            for (int x = clipped.Left; x < clipped.Right; x++)
            {
                mask._coverage[(y * size.Width) + x] = byte.MaxValue;
            }
        }

        mask.Bounds = clipped;
        return mask;
    }

    /// <summary>Reads the coverage of a pixel; out-of-bounds coordinates read as zero.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns>The coverage, 0 (unselected) to 255 (fully selected).</returns>
    public byte GetCoverage(int x, int y) =>
        Size.Contains(x, y) ? _coverage[(y * Size.Width) + x] : (byte)0;

    /// <summary>
    /// Determines whether a pixel may be edited. This is the question every drawing tool
    /// asks; see the type remarks for why it is asked even when no selection exists.
    /// </summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <returns><see langword="true"/> if the pixel is at least partially selected.</returns>
    public bool IsSelected(int x, int y) => GetCoverage(x, y) > 0;

    /// <summary>Sets the coverage of a pixel and grows <see cref="Bounds"/> accordingly.</summary>
    /// <param name="x">The horizontal coordinate.</param>
    /// <param name="y">The vertical coordinate.</param>
    /// <param name="coverage">The coverage to store.</param>
    public void SetCoverage(int x, int y, byte coverage)
    {
        if (!Size.Contains(x, y))
        {
            return;
        }

        _coverage[(y * Size.Width) + x] = coverage;

        if (coverage > 0)
        {
            Bounds = Bounds.Union(x, y);
        }
    }

    /// <summary>Creates an independent copy of this mask.</summary>
    /// <returns>A new mask with the same contents.</returns>
    public SelectionMask Clone() => new(Size, (byte[])_coverage.Clone(), Bounds);

    /// <summary>
    /// Recomputes <see cref="Bounds"/> from the coverage actually stored.
    /// </summary>
    /// <remarks>
    /// <see cref="SetCoverage"/> can only grow the bounds, because growing is all that marking
    /// ever needs and testing the whole mask on every pixel of a drag would be absurd. A tool
    /// that can <em>unmark</em> pixels leaves the bounds too large, which shows up as marching
    /// ants around a region that is no longer selected and as a mask that never reports itself
    /// empty. Such a tool calls this once, when the drag ends.
    /// </remarks>
    public void RecalculateBounds()
    {
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;

        for (int y = 0; y < Size.Height; y++)
        {
            int row = y * Size.Width;

            for (int x = 0; x < Size.Width; x++)
            {
                if (_coverage[row + x] == 0)
                {
                    continue;
                }

                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        Bounds = right > left && bottom > top
            ? ImageRect.FromEdges(left, top, right, bottom)
            : ImageRect.Empty;
    }
}
